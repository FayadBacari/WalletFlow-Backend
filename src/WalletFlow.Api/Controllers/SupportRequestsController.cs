using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Security;
using WalletFlow.Application.Support;

namespace WalletFlow.Api.Controllers;

// Demandes de support (GR Admin). Les routes de l'utilisateur connecté n'ont aucun attribut de sécurité : toute route demande
// par défaut une session et le jeton anti-CSRF (voir Program.cs), et elles ne touchent que les demandes du compte de la session
// (SS §4). La route des visiteurs est appelée sans session : elle n'agit au nom de personne, et n'a donc ni besoin d'être
// connecté, ni besoin du jeton anti-CSRF. Comme toute route, elle n'accepte que du JSON (SS §6).
[ApiController]
[Route("api/support-requests")]
public class SupportRequestsController : ControllerBase
{
    public const string VisitorRequestSentMessage = "Ta demande est envoyée. Le support te répondra par e-mail, à l'adresse donnée.";

    private readonly SupportRequestSendingService sendingService;
    private readonly VisitorSupportRequestService visitorService;
    private readonly SupportRequestReadingService readingService;
    private readonly ForeignAccessJournalService foreignAccessJournal;

    // Reçoit les cas d'utilisation du support, et le journal des accès refusés.
    public SupportRequestsController(
        SupportRequestSendingService sendingService,
        VisitorSupportRequestService visitorService,
        SupportRequestReadingService readingService,
        ForeignAccessJournalService foreignAccessJournal)
    {
        this.sendingService = sendingService;
        this.visitorService = visitorService;
        this.readingService = readingService;
        this.foreignAccessJournal = foreignAccessJournal;
    }

    // GET api/support-requests : les demandes de l'utilisateur, la plus récente d'abord, avec leur état et leur nombre de
    // réponses. Ce sont des données personnelles : aucun navigateur ni intermédiaire ne doit les garder.
    [HttpGet]
    [ProducesResponseType<SupportRequestListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default)
    {
        var requests = await readingService.ListAsync(SessionClaims.ReadUserId(User), cancellationToken);
        var items = requests
            .Select(request => new SupportRequestItemResponse(request.Id, request.SentAt, WebsiteNames.Of(request.Status), request.ResponseCount))
            .ToList();

        return Ok(new SupportRequestListResponse(items));
    }

    // GET api/support-requests/{requestId} : une demande de l'utilisateur, avec son message et ses réponses. Celle d'un autre,
    // ou d'un visiteur, répond comme une demande qui n'existe pas (SS §4).
    [HttpGet("{requestId:guid}")]
    [ProducesResponseType<SupportRequestDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Read(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await readingService.ReadAsync(SessionClaims.ReadUserId(User), requestId, cancellationToken);

        if (request == null)
        {
            await foreignAccessJournal.RecordIfForeignAsync(
                SessionClaims.ReadUserId(User),
                HttpContext.Connection.RemoteIpAddress,
                OwnedResource.SupportRequest,
                requestId,
                cancellationToken);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(CreateResponse(request));
    }

    // POST api/support-requests : l'utilisateur connecté écrit au support, 10 demandes au plus sur les dernières 24 heures.
    [HttpPost]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumSupportRequestBytes)]
    [ProducesResponseType<SupportRequestDetailsResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send(SupportMessageRequest request, CancellationToken cancellationToken = default)
    {
        var result = await sendingService.SendAsync(SessionClaims.ReadUserId(User), request.Message, cancellationToken);

        if (result is SupportRequestSendingResult.Sent sent)
        {
            return StatusCode(StatusCodes.Status201Created, CreateResponse(sent.Request));
        }

        if (result is SupportRequestSendingResult.MessageBreaksRules)
        {
            ModelState.AddModelError(nameof(SupportMessageRequest.Message), ErrorMessages.SupportMessageBreaksRules);
            return ValidationProblem();
        }

        // Au-delà de la limite, la réponse est celle de toutes les limites de débit (SS §6).
        if (result is SupportRequestSendingResult.TooManyRequests)
        {
            return this.AnswerTooManyRequests(RetryDelays.SupportRequests);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.AccountChangedMeanwhile);
    }

    // POST api/support-requests/visitor : un visiteur non connecté écrit au support, avec l'adresse où lui répondre.
    // 3 demandes par heure et par adresse IP (SS §6). Aucun e-mail ne part avant la réponse du support.
    [HttpPost("visitor")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting(RateLimitingConfiguration.VisitorSupportPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumSupportRequestBytes)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendAsVisitor(VisitorSupportMessageRequest request, CancellationToken cancellationToken = default)
    {
        var result = await visitorService.SendAsync(request.ContactEmail, request.Message, cancellationToken);

        if (result is VisitorSupportRequestResult.Sent)
        {
            return Ok(new MessageResponse(VisitorRequestSentMessage));
        }

        if (result is VisitorSupportRequestResult.RefusedByRules refusal)
        {
            if (refusal.IsContactEmailInvalid)
            {
                ModelState.AddModelError(nameof(VisitorSupportMessageRequest.ContactEmail), ErrorMessages.InvalidEmailAddress);
            }

            if (refusal.IsMessageInvalid)
            {
                ModelState.AddModelError(nameof(VisitorSupportMessageRequest.Message), ErrorMessages.SupportMessageBreaksRules);
            }

            return ValidationProblem();
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.SupportRequestNotSaved);
    }

    // Donne la demande telle que le site la reçoit.
    private static SupportRequestDetailsResponse CreateResponse(SupportRequestDetails request)
    {
        return new SupportRequestDetailsResponse(
            request.Id,
            request.SentAt,
            WebsiteNames.Of(request.Status),
            request.Message,
            request.Responses.Select(response => new SupportAnswerResponse(response.Content, response.SentAt)).ToList());
    }
}
