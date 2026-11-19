using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Security;
using WalletFlow.Application.Suggestions;

namespace WalletFlow.Api.Controllers;

// Suggestions d'épargne de l'utilisateur connecté : la liste avec les économies réalisées, l'acceptation et l'ignorance (GR Sacs).
// Ces routes demandent une session, que [Authorize] redit, et le jeton anti-CSRF, comme toute route
// (voir Program.cs). La suggestion d'un autre utilisateur répond 404, comme une adresse qui n'existe pas (SS §4).
[ApiController]
[Route("api/suggestions")]
[Authorize]
public class SuggestionsController : ControllerBase
{
    private readonly SuggestionListService listService;
    private readonly SuggestionAcceptanceService acceptanceService;
    private readonly SuggestionDismissalService dismissalService;
    private readonly ForeignAccessJournalService foreignAccessJournal;

    // Reçoit les cas d'utilisation des suggestions.
    public SuggestionsController(
        SuggestionListService listService,
        SuggestionAcceptanceService acceptanceService,
        SuggestionDismissalService dismissalService,
        ForeignAccessJournalService foreignAccessJournal)
    {
        this.listService = listService;
        this.acceptanceService = acceptanceService;
        this.dismissalService = dismissalService;
        this.foreignAccessJournal = foreignAccessJournal;
    }

    // GET api/suggestions : les suggestions du dernier mois complet et ce que l'utilisateur a économisé. La tâche horaire les
    // calcule à partir du 5 : la lecture ne change rien (SS §6). Ce sont des données financières : aucun navigateur ni
    // intermédiaire ne doit les garder.
    [HttpGet]
    [EnableRateLimiting(RateLimitingConfiguration.CostlyReadPolicy)]
    [ProducesResponseType<SuggestionListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default)
    {
        var list = await listService.ListAsync(SessionClaims.ReadUserId(User), cancellationToken);
        var suggestions = list.Suggestions
            .Select(view => new SuggestionResponse(
                view.Id,
                WebsiteNames.Of(view.Rule),
                view.Month,
                view.CategoryId,
                view.CategoryName,
                view.MonthlySavingsInCents))
            .ToList();

        return Ok(new SuggestionListResponse(list.Month, suggestions, list.SavedInCents));
    }

    // POST api/suggestions/{id}/accept : place le montant choisi dans le sac choisi, et accepte la suggestion.
    [HttpPost("{suggestionId:guid}/accept")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumSuggestionRequestBytes)]
    [ProducesResponseType<BagMovementResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept(Guid suggestionId, SuggestionAcceptanceRequest request, CancellationToken cancellationToken = default)
    {
        var result = await acceptanceService.AcceptAsync(
            SessionClaims.ReadUserId(User),
            suggestionId,
            request.BagId,
            request.AmountInCents,
            cancellationToken);

        if (result is SuggestionAcceptanceResult.Accepted accepted)
        {
            var movement = accepted.Movement;
            var response = new BagMovementResponse(
                movement.Id,
                movement.MovedAt,
                movement.AmountInCents,
                movement.SourceBagId,
                movement.DestinationBagId);

            return StatusCode(StatusCodes.Status201Created, response);
        }

        if (result is SuggestionAcceptanceResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Suggestion, suggestionId, cancellationToken);

            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is SuggestionAcceptanceResult.BagNotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Bag, request.BagId, cancellationToken);

            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is SuggestionAcceptanceResult.AlreadyAnswered)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.SuggestionAlreadyAnswered);
        }

        if (result is SuggestionAcceptanceResult.AmountBreaksRules)
        {
            return AnswerFieldRefused(nameof(SuggestionAcceptanceRequest.AmountInCents), ErrorMessages.SuggestionAmountBreaksRules);
        }

        if (result is SuggestionAcceptanceResult.AvailableMoneyTooLow)
        {
            return AnswerFieldRefused(nameof(SuggestionAcceptanceRequest.AmountInCents), ErrorMessages.AvailableMoneyTooLow);
        }

        if (result is SuggestionAcceptanceResult.TooManyMovements)
        {
            return this.AnswerTooManyRequests(RetryDelays.BagMovements);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.BagsChangedMeanwhile);
    }

    // POST api/suggestions/{id}/ignore : ignore la suggestion. Sa règle ne propose plus rien pendant 30 jours.
    [HttpPost("{suggestionId:guid}/ignore")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumSuggestionRequestBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Ignore(Guid suggestionId, CancellationToken cancellationToken = default)
    {
        var result = await dismissalService.DismissAsync(SessionClaims.ReadUserId(User), suggestionId, cancellationToken);

        if (result is SuggestionDismissalResult.Dismissed)
        {
            return NoContent();
        }

        if (result is SuggestionDismissalResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Suggestion, suggestionId, cancellationToken);

            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is SuggestionDismissalResult.AlreadyAnswered)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.SuggestionAlreadyAnswered);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.BagsChangedMeanwhile);
    }

    // Note au journal si la ressource visée est à un autre utilisateur (SS §11). La réponse reste la même dans tous les cas.
    private async Task RecordForeignAccessAsync(OwnedResource resource, Guid resourceId, CancellationToken cancellationToken = default)
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
}
