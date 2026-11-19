using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Security;

namespace WalletFlow.Api.Controllers;

// Connexions bancaires de l'utilisateur connecté, en lecture seule (GR Comptes, SS §7) : la liste des banques, la connexion
// et son renouvellement, les comptes et leurs soldes, le solde total, leur mise à jour, et la déconnexion.
// Ces routes demandent une session, que [Authorize] redit, et le jeton anti-CSRF, comme toute route (voir Program.cs).
// La connexion d'un autre utilisateur répond 404, comme une adresse qui n'existe pas (SS §4). Les routes qui appellent le
// prestataire partagent une limite par session (SS §6) : le prestataire limite les appels de toute l'application.
[ApiController]
[Route("api")]
[Authorize]
public class BankConnectionsController : ControllerBase
{
    public const string DisconnectedMessage = "La banque est déconnectée. Ses comptes et leurs transactions sont supprimés de WalletFlow.";

    private readonly BankListService bankListService;
    private readonly BankAuthorizationStartService authorizationStartService;
    private readonly BankConnectionFinishService finishService;
    private readonly BankConnectionListService connectionListService;
    private readonly BankBalanceUpdateService balanceUpdateService;
    private readonly BankDisconnectionService disconnectionService;
    private readonly BankBalanceTotalService balanceTotalService;
    private readonly ForeignAccessJournalService foreignAccessJournal;

    // Reçoit les cas d'utilisation des connexions bancaires.
    public BankConnectionsController(
        BankListService bankListService,
        BankAuthorizationStartService authorizationStartService,
        BankConnectionFinishService finishService,
        BankConnectionListService connectionListService,
        BankBalanceUpdateService balanceUpdateService,
        BankDisconnectionService disconnectionService,
        BankBalanceTotalService balanceTotalService,
        ForeignAccessJournalService foreignAccessJournal)
    {
        this.bankListService = bankListService;
        this.authorizationStartService = authorizationStartService;
        this.finishService = finishService;
        this.connectionListService = connectionListService;
        this.balanceUpdateService = balanceUpdateService;
        this.disconnectionService = disconnectionService;
        this.balanceTotalService = balanceTotalService;
        this.foreignAccessJournal = foreignAccessJournal;
    }

    // GET api/banks : les banques que l'utilisateur peut connecter, dans l'ordre alphabétique.
    [HttpGet("banks")]
    [EnableRateLimiting(RateLimitingConfiguration.BankPolicy)]
    [ProducesResponseType<List<BankResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.BankUnavailableDescription)]
    public async Task<IActionResult> ListBanks(CancellationToken cancellationToken = default)
    {
        var result = await bankListService.ListAsync(cancellationToken);

        if (result is BankListResult.Listed listed)
        {
            return Ok(listed.Banks.Select(bank => new BankResponse(bank.Name)).ToList());
        }

        return AnswerServiceUnavailable();
    }

    // POST api/bank-connections : commence la connexion d'une banque, et donne l'adresse de sa page.
    [HttpPost("bank-connections")]
    [EnableRateLimiting(RateLimitingConfiguration.BankPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumBankRequestBytes)]
    [ProducesResponseType<BankAddressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.BankUnavailableDescription)]
    public async Task<IActionResult> StartConnection(StartBankConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var result = await authorizationStartService.StartNewAsync(SessionClaims.ReadUserId(User), request.BankName, cancellationToken);

        return AnswerAuthorizationStart(result);
    }

    // POST api/bank-connections/{id}/renew : commence le renouvellement de l'autorisation d'une connexion (GR Comptes).
    [HttpPost("bank-connections/{connectionId:guid}/renew")]
    [EnableRateLimiting(RateLimitingConfiguration.BankPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumBankRequestBytes)]
    [ProducesResponseType<BankAddressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.BankUnavailableDescription)]
    public async Task<IActionResult> StartRenewal(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var result = await authorizationStartService.StartRenewalAsync(SessionClaims.ReadUserId(User), connectionId, cancellationToken);

        if (result is BankAuthorizationStartResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.BankConnection, connectionId, cancellationToken);
        }

        return AnswerAuthorizationStart(result);
    }

    // POST api/bank-connections/finish : termine la connexion avec le code et le state renvoyés par la banque.
    // La banque renvoie l'utilisateur sur une page du site, qui envoie ici ce qu'elle a lu dans son adresse :
    // une requête GET ne doit jamais rien modifier (SS §6).
    [HttpPost("bank-connections/finish")]
    [EnableRateLimiting(RateLimitingConfiguration.BankPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumBankRequestBytes)]
    [ProducesResponseType<BankConnectionFinishResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.BankUnavailableDescription)]
    public async Task<IActionResult> Finish(FinishBankConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var result = await finishService.FinishAsync(SessionClaims.ReadUserId(User), request.Code, request.State, ReadPresence(), cancellationToken);

        if (result is BankConnectionFinishResult.Connected connected)
        {
            return Ok(new BankConnectionFinishResponse(connected.ConnectionId, false, connected.AddedAccountCount, connected.SkippedAccountNames));
        }

        if (result is BankConnectionFinishResult.Renewed renewed)
        {
            return Ok(new BankConnectionFinishResponse(renewed.ConnectionId, true, renewed.AddedAccountCount, renewed.SkippedAccountNames));
        }

        if (result is BankConnectionFinishResult.LinkNoLongerValid)
        {
            return AnswerFieldRefused(nameof(FinishBankConnectionRequest.State), ErrorMessages.BankLinkNoLongerValid);
        }

        if (result is BankConnectionFinishResult.RefusedByBank)
        {
            return AnswerFieldRefused(nameof(FinishBankConnectionRequest.Code), ErrorMessages.BankRefusedAuthorization);
        }

        if (result is BankConnectionFinishResult.NoAccountAdded)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.NoBankAccountAdded);
        }

        if (result is BankConnectionFinishResult.ConnectionGone)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is BankConnectionFinishResult.NotSaved)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.BankConnectionsChangedMeanwhile);
        }

        return AnswerServiceUnavailable();
    }

    // GET api/bank-connections : les banques connectées, leurs comptes, leurs soldes et la date de chaque solde.
    // Ce sont des données financières : aucun navigateur ni intermédiaire ne doit les garder.
    [HttpGet("bank-connections")]
    [ProducesResponseType<List<BankConnectionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListConnections(CancellationToken cancellationToken = default)
    {
        return Ok(await CreateConnectionResponsesAsync(cancellationToken));
    }

    // POST api/bank-connections/refresh : met à jour les soldes, à l'ouverture de l'application, une fois par minute au plus.
    // Répond la liste à jour, ou 429 si la dernière mise à jour date de moins d'une minute (SS §6).
    [HttpPost("bank-connections/refresh")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumBankRequestBytes)]
    [ProducesResponseType<List<BankConnectionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken = default)
    {
        var result = await balanceUpdateService.UpdateForUserAsync(SessionClaims.ReadUserId(User), ReadPresence(), cancellationToken);

        if (result is BankUpdateResult.TooManyUpdates)
        {
            return this.AnswerTooManyRequests(RetryDelays.BankUpdate);
        }

        return Ok(await CreateConnectionResponsesAsync(cancellationToken));
    }

    // GET api/balance : le solde total, tous comptes confondus (GR Comptes), avec la date du plus ancien des soldes additionnés.
    // Chaque compte compte avec son dernier solde connu, même quand sa banque ne répond pas. Données financières : jamais gardées.
    [HttpGet("balance")]
    [ProducesResponseType<BalanceTotalResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReadBalanceTotal(CancellationToken cancellationToken = default)
    {
        var total = await balanceTotalService.ReadAsync(SessionClaims.ReadUserId(User), cancellationToken);

        return Ok(new BalanceTotalResponse(total.TotalInCents, total.OldestBalanceUpdatedAt, total.AccountCount));
    }

    // DELETE api/bank-connections/{id} : révoque l'autorisation, puis supprime la banque, ses comptes et leurs transactions.
    // Le site demande d'abord à l'utilisateur de confirmer (GR Comptes). Cette route ne lit aucun corps.
    [HttpDelete("bank-connections/{connectionId:guid}")]
    [EnableRateLimiting(RateLimitingConfiguration.BankPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumBankRequestBytes)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.BankUnavailableDescription)]
    public async Task<IActionResult> Disconnect(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var result = await disconnectionService.DisconnectAsync(
            SessionClaims.ReadUserId(User),
            connectionId,
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        if (result is BankDisconnectionResult.Disconnected)
        {
            return Ok(new MessageResponse(DisconnectedMessage));
        }

        if (result is BankDisconnectionResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.BankConnection, connectionId, cancellationToken);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is BankDisconnectionResult.NotSaved)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.BankConnectionsChangedMeanwhile);
        }

        return AnswerServiceUnavailable();
    }

    // Répond au début d'une autorisation : l'adresse de la banque, ou la raison du refus.
    private IActionResult AnswerAuthorizationStart(BankAuthorizationStartResult result)
    {
        if (result is BankAuthorizationStartResult.Started started)
        {
            return Ok(new BankAddressResponse(started.BankAddress));
        }

        if (result is BankAuthorizationStartResult.UnknownBank)
        {
            return AnswerFieldRefused(nameof(StartBankConnectionRequest.BankName), ErrorMessages.BankNotOffered);
        }

        if (result is BankAuthorizationStartResult.NotFound)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        return AnswerServiceUnavailable();
    }

    // Note au journal si la ressource visée est à un autre utilisateur (SS §11). La réponse reste la même dans tous les cas.
    private async Task RecordForeignAccessAsync(OwnedResource resource, Guid? resourceId, CancellationToken cancellationToken = default)
    {
        await foreignAccessJournal.RecordIfForeignAsync(
            SessionClaims.ReadUserId(User),
            HttpContext.Connection.RemoteIpAddress,
            resource,
            resourceId,
            cancellationToken);
    }

    // Donne le message d'un champ refusé.
    private ActionResult AnswerFieldRefused(string field, string message)
    {
        ModelState.AddModelError(field, message);

        return ValidationProblem();
    }

    // Le prestataire ou la banque ne répond pas : 503, l'utilisateur réessaiera plus tard.
    private ObjectResult AnswerServiceUnavailable()
    {
        return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: ErrorMessages.BankServiceUnavailable);
    }

    // L'utilisateur est là : son adresse IP et son navigateur partent vers sa banque avec la lecture (DSP2).
    private UserPresence ReadPresence()
    {
        return new UserPresence(HttpContext.Connection.RemoteIpAddress, Request.Headers.UserAgent.ToString());
    }

    // Lit les connexions de l'utilisateur, et les traduit en réponses de l'API.
    private async Task<List<BankConnectionResponse>> CreateConnectionResponsesAsync(CancellationToken cancellationToken = default)
    {
        var views = await connectionListService.ListAsync(SessionClaims.ReadUserId(User), cancellationToken);
        var responses = new List<BankConnectionResponse>();

        foreach (var view in views)
        {
            var accounts = view.Accounts
                .Select(account => new BankAccountResponse(account.Id, account.Name, account.BalanceInCents, account.BalanceUpdatedAt))
                .ToList();

            responses.Add(new BankConnectionResponse(
                view.Connection.Id,
                view.Connection.BankName,
                WebsiteNames.Of(view.Status),
                view.Connection.AuthorizationExpiresAt,
                view.IsEndingSoon,
                accounts));
        }

        return responses;
    }
}
