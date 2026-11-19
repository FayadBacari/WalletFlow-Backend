using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Filters;
using WalletFlow.Application.Administration;

namespace WalletFlow.Api.Controllers;

// Administration (GR Admin, SS §8). Chaque route demande une session, que [Authorize] redit, et le jeton anti-CSRF, comme
// toute route (voir Program.cs), puis le filtre AdministratorOnlyFilter la réserve à l'administrateur qui a activé la double
// authentification. L'administrateur ne voit ni compte bancaire, ni opération, ni solde, ni sac : seulement l'adresse et l'état
// d'un compte, les demandes de support et des statistiques sans argent. Il agit sur tous les comptes : un identifiant reçu ici
// n'est jamais celui « d'un autre ».
[ApiController]
[Route("api/admin")]
[Authorize]
[ServiceFilter(typeof(AdministratorOnlyFilter), Order = AdministratorOnlyFilter.OrderAfterCsrfCheck)]
public class AdministrationController : ControllerBase
{
    public const string TwoFactorLinkSentMessage = "Le lien part à l'adresse du compte. Il est valable 24 heures.";

    private readonly AdminAccountSearchService searchService;
    private readonly AdminAccountUnlockService unlockService;
    private readonly AdminTwoFactorLinkService twoFactorLinkService;
    private readonly AdminSupportService supportService;
    private readonly AdminStatisticsService statisticsService;

    // Reçoit les cas d'utilisation de l'administration.
    public AdministrationController(
        AdminAccountSearchService searchService,
        AdminAccountUnlockService unlockService,
        AdminTwoFactorLinkService twoFactorLinkService,
        AdminSupportService supportService,
        AdminStatisticsService statisticsService)
    {
        this.searchService = searchService;
        this.unlockService = unlockService;
        this.twoFactorLinkService = twoFactorLinkService;
        this.supportService = supportService;
        this.statisticsService = statisticsService;
    }

    // POST api/admin/accounts/search : le compte qui utilise exactement cette adresse, ou null. Aucune liste de comptes :
    // l'administrateur ne peut pas parcourir les utilisateurs. Chaque recherche est notée au journal. L'adresse cherchée passe
    // dans le corps de la requête, jamais dans l'adresse de la route, qui finit dans les journaux d'un proxy (SS §7). Et comme
    // toute écriture, une recherche notée au journal demande le jeton anti-CSRF, qu'un lien piégé n'a pas (SS §6).
    [HttpPost("accounts/search")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    [ProducesResponseType<AdminAccountSearchResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAccount(AdminAccountSearchRequest request, CancellationToken cancellationToken = default)
    {
        var result = await searchService.SearchAsync(SessionClaims.ReadUserId(User), request.Email, HttpContext.Connection.RemoteIpAddress, cancellationToken);

        if (result is AdminAccountSearchResult.Found found)
        {
            return Ok(new AdminAccountSearchResponse(CreateResponse(found.Account)));
        }

        if (result is AdminAccountSearchResult.NotFound)
        {
            return Ok(new AdminAccountSearchResponse(null));
        }

        return AnswerFieldRefused(nameof(AdminAccountSearchRequest.Email), ErrorMessages.InvalidEmailAddress);
    }

    // POST api/admin/accounts/{accountId}/unlock : débloque le compte, comme le lien de déblocage.
    [HttpPost("accounts/{accountId:guid}/unlock")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAdministrationRequestBytes)]
    [ProducesResponseType<AdminAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlockAccount(Guid accountId, CancellationToken cancellationToken = default)
    {
        var result = await unlockService.UnlockAsync(SessionClaims.ReadUserId(User), accountId, HttpContext.Connection.RemoteIpAddress, cancellationToken);

        return result switch
        {
            AdminAccountUnlockResult.Unlocked unlocked => Ok(CreateResponse(unlocked.Account)),
            AdminAccountUnlockResult.NotFound => Problem(statusCode: StatusCodes.Status404NotFound),
            AdminAccountUnlockResult.NotLocked => Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.AccountNotLocked),
            _ => Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.AccountChangedMeanwhile)
        };
    }

    // POST api/admin/accounts/{accountId}/two-factor-link : fait envoyer à l'adresse du compte le lien qui désactive sa double
    // authentification. L'administrateur ne voit ni ce lien ni le mot de passe.
    [HttpPost("accounts/{accountId:guid}/two-factor-link")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAdministrationRequestBytes)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendTwoFactorLink(Guid accountId, CancellationToken cancellationToken = default)
    {
        var result = await twoFactorLinkService.SendAsync(SessionClaims.ReadUserId(User), accountId, HttpContext.Connection.RemoteIpAddress, cancellationToken);

        return result switch
        {
            AdminTwoFactorLinkResult.Sent => Ok(new MessageResponse(TwoFactorLinkSentMessage)),
            AdminTwoFactorLinkResult.NotFound => Problem(statusCode: StatusCodes.Status404NotFound),
            AdminTwoFactorLinkResult.TwoFactorNotEnabled => Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.AccountTwoFactorNotEnabled),
            AdminTwoFactorLinkResult.PasswordChangedRecently => Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.AccountPasswordChangedRecently),
            _ => Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.AccountChangedMeanwhile)
        };
    }

    // GET api/admin/support-requests?status=sent : les demandes dans cet état (« sent », « inProgress » ou « resolved »),
    // la plus ancienne d'abord.
    [HttpGet("support-requests")]
    [ProducesResponseType<AdminSupportRequestListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSupportRequests([FromQuery] string? status, CancellationToken cancellationToken = default)
    {
        if (!WebsiteNames.TryReadSupportRequestStatus(status, out var supportRequestStatus))
        {
            return AnswerFieldRefused("status", ErrorMessages.SupportRequestStatusUnknown);
        }

        var requests = await supportService.ListAsync(supportRequestStatus, cancellationToken);
        var items = requests
            .Select(request => new AdminSupportRequestItemResponse(
                request.Id,
                request.SentAt,
                WebsiteNames.Of(request.Status),
                request.ReplyAddress,
                request.AccountId,
                request.ResponseCount))
            .ToList();

        return Ok(new AdminSupportRequestListResponse(items));
    }

    // GET api/admin/support-requests/{requestId} : une demande avec son message et ses réponses.
    [HttpGet("support-requests/{requestId:guid}")]
    [ProducesResponseType<AdminSupportRequestDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReadSupportRequest(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await supportService.ReadAsync(requestId, cancellationToken);

        if (request == null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(CreateResponse(request));
    }

    // POST api/admin/support-requests/{requestId}/responses : répond à la demande. La réponse part par e-mail avec son texte, et la
    // demande passe « en cours », même si elle était résolue.
    [HttpPost("support-requests/{requestId:guid}/responses")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumSupportRequestBytes)]
    [ProducesResponseType<AdminSupportRequestDetailsResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AnswerSupportRequest(Guid requestId, AdminAnswerRequest request, CancellationToken cancellationToken = default)
    {
        var result = await supportService.AnswerAsync(
            SessionClaims.ReadUserId(User),
            requestId,
            request.Content,
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        return result switch
        {
            AdminSupportAnswerResult.Answered answered => StatusCode(StatusCodes.Status201Created, CreateResponse(answered.Request)),
            AdminSupportAnswerResult.NotFound => Problem(statusCode: StatusCodes.Status404NotFound),
            AdminSupportAnswerResult.ContentBreaksRules => AnswerFieldRefused(nameof(AdminAnswerRequest.Content), ErrorMessages.SupportAnswerBreaksRules),
            _ => Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.SupportRequestChangedMeanwhile)
        };
    }

    // POST api/admin/support-requests/{requestId}/resolve : marque la demande « résolue », avec ou sans réponse.
    [HttpPost("support-requests/{requestId:guid}/resolve")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAdministrationRequestBytes)]
    [ProducesResponseType<AdminSupportRequestDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveSupportRequest(Guid requestId, CancellationToken cancellationToken = default)
    {
        var result = await supportService.ResolveAsync(SessionClaims.ReadUserId(User), requestId, HttpContext.Connection.RemoteIpAddress, cancellationToken);

        return result switch
        {
            AdminSupportResolutionResult.Resolved resolved => Ok(CreateResponse(resolved.Request)),
            AdminSupportResolutionResult.NotFound => Problem(statusCode: StatusCodes.Status404NotFound),
            AdminSupportResolutionResult.AlreadyResolved => Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.SupportRequestAlreadyResolved),
            _ => Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.SupportRequestChangedMeanwhile)
        };
    }

    // GET api/admin/statistics : les comptes par état, les connexions bancaires actives et les demandes par état.
    [HttpGet("statistics")]
    [ProducesResponseType<AdminStatisticsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReadStatistics(CancellationToken cancellationToken = default)
    {
        var statistics = await statisticsService.ReadAsync(cancellationToken);

        return Ok(new AdminStatisticsResponse(
            new AccountsByStateResponse(
                statistics.NotConfirmedAccounts,
                statistics.ActiveAccounts,
                statistics.LockedAccounts,
                statistics.AccountsBeingDeleted),
            statistics.ActiveBankConnections,
            new SupportRequestsByStatusResponse(
                statistics.SentRequests,
                statistics.RequestsInProgress,
                statistics.ResolvedRequests)));
    }

    // Donne le compte tel que le site le reçoit.
    private static AdminAccountResponse CreateResponse(AdminAccountView account)
    {
        return new AdminAccountResponse(account.Id, account.Email, WebsiteNames.Of(account.State), account.IsTwoFactorEnabled);
    }

    // Donne la demande telle que le site la reçoit.
    private static AdminSupportRequestDetailsResponse CreateResponse(AdminSupportRequestDetails request)
    {
        return new AdminSupportRequestDetailsResponse(
            request.Id,
            request.SentAt,
            WebsiteNames.Of(request.Status),
            request.ReplyAddress,
            request.AccountId,
            request.Message,
            request.Responses.Select(response => new SupportAnswerResponse(response.Content, response.SentAt)).ToList());
    }

    // Donne le message d'un champ refusé.
    private ActionResult AnswerFieldRefused(string field, string message)
    {
        ModelState.AddModelError(field, message);

        return ValidationProblem();
    }
}
