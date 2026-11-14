using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Bags;
using WalletFlow.Application.Security;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Api.Controllers;

// Sacs de l'utilisateur connecté : la liste, la création, le nom, l'objectif, la suppression, les déplacements d'argent
// et l'historique (GR Sacs). Ces routes n'ont aucun attribut de sécurité : toute route demande par défaut une session
// et le jeton anti-CSRF (voir Program.cs). Le sac d'un autre utilisateur répond 404, comme une adresse qui n'existe pas :
// rien ne dit qu'il existe (SS §4).
[ApiController]
[Route("api/bags")]
public class BagsController : ControllerBase
{
    public const string BagDeletedMessage = "Le sac est supprimé. Son argent est revenu dans l'argent disponible.";

    private readonly BagListService listService;
    private readonly BagCreationService creationService;
    private readonly BagRenameService renameService;
    private readonly BagGoalService goalService;
    private readonly BagDeletionService deletionService;
    private readonly BagMovementService movementService;
    private readonly BagHistoryService historyService;
    private readonly ForeignAccessJournalService foreignAccessJournal;

    // Reçoit les cas d'utilisation des sacs.
    public BagsController(
        BagListService listService,
        BagCreationService creationService,
        BagRenameService renameService,
        BagGoalService goalService,
        BagDeletionService deletionService,
        BagMovementService movementService,
        BagHistoryService historyService,
        ForeignAccessJournalService foreignAccessJournal)
    {
        this.listService = listService;
        this.creationService = creationService;
        this.renameService = renameService;
        this.goalService = goalService;
        this.deletionService = deletionService;
        this.movementService = movementService;
        this.historyService = historyService;
        this.foreignAccessJournal = foreignAccessJournal;
    }

    // GET api/bags : les sacs avec leur solde et leur progression, le solde total, le total des sacs et l'argent disponible.
    // Ce sont des données financières : aucun navigateur ni intermédiaire ne doit les garder.
    [HttpGet]
    [EnableRateLimiting(RateLimitingConfiguration.CostlyReadPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List()
    {
        var summary = await listService.ListAsync(SessionClaims.ReadUserId(User));
        var bags = summary.Bags.Select(CreateResponse).ToList();

        return Ok(new BagListResponse(
            bags,
            summary.TotalBalanceInCents,
            summary.BagsTotalInCents,
            summary.AvailableInCents,
            summary.OldestBalanceUpdatedAt));
    }

    // POST api/bags : crée un sac vide avec ce nom, et cet objectif s'il y en a un.
    [HttpPost]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumBagRequestBytes)]
    public async Task<IActionResult> Create(BagCreationRequest request)
    {
        var result = await creationService.CreateAsync(
            SessionClaims.ReadUserId(User),
            request.Name,
            request.TargetAmountInCents,
            request.TargetDate);

        if (result is BagCreationResult.Created created)
        {
            return StatusCode(StatusCodes.Status201Created, CreateResponse(created.Bag));
        }

        if (result is BagCreationResult.NameBreaksRules)
        {
            return AnswerFieldRefused(nameof(BagCreationRequest.Name), ErrorMessages.BagNameBreaksRules);
        }

        if (result is BagCreationResult.GoalBreaksRules goalRefused)
        {
            return AnswerGoalRefused(goalRefused.Problem);
        }

        if (result is BagCreationResult.TooManyBags)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.TooManyBags);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.BagsChangedMeanwhile);
    }

    // PUT api/bags/{id}/name : donne un nouveau nom au sac.
    [HttpPut("{bagId:guid}/name")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumBagRequestBytes)]
    public async Task<IActionResult> Rename(Guid bagId, BagNameRequest request)
    {
        var result = await renameService.RenameAsync(SessionClaims.ReadUserId(User), bagId, request.Name);

        if (result is BagRenameResult.Renamed renamed)
        {
            return Ok(CreateResponse(renamed.Bag));
        }

        if (result is BagRenameResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Bag, bagId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is BagRenameResult.NameBreaksRules)
        {
            return AnswerFieldRefused(nameof(BagNameRequest.Name), ErrorMessages.BagNameBreaksRules);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.BagsChangedMeanwhile);
    }

    // PUT api/bags/{id}/goal : fixe l'objectif du sac, ou l'enlève avec deux null.
    [HttpPut("{bagId:guid}/goal")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumBagRequestBytes)]
    public async Task<IActionResult> ReplaceGoal(Guid bagId, BagGoalRequest request)
    {
        var result = await goalService.ReplaceAsync(
            SessionClaims.ReadUserId(User),
            bagId,
            request.TargetAmountInCents,
            request.TargetDate);

        if (result is BagGoalResult.Changed changed)
        {
            return Ok(CreateResponse(changed.Bag));
        }

        if (result is BagGoalResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Bag, bagId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is BagGoalResult.GoalBreaksRules goalRefused)
        {
            return AnswerGoalRefused(goalRefused.Problem);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.BagsChangedMeanwhile);
    }

    // DELETE api/bags/{id} : supprime le sac. Son argent revient dans l'argent disponible.
    // Cette route ne lit aucun corps : la limite empêche seulement qu'on lui en envoie un énorme.
    [HttpDelete("{bagId:guid}")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumBagRequestBytes)]
    public async Task<IActionResult> Delete(Guid bagId)
    {
        var result = await deletionService.DeleteAsync(SessionClaims.ReadUserId(User), bagId);

        if (result is BagDeletionResult.Deleted)
        {
            return Ok(new MessageResponse(BagDeletedMessage));
        }

        if (result is BagDeletionResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Bag, bagId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.BagsChangedMeanwhile);
    }

    // POST api/bags/movements : déplace de l'argent disponible vers un sac, d'un sac à un autre, ou d'un sac vers l'argent disponible.
    // Au-delà de 1 000 déplacements sur les dernières 24 heures, la réponse est « trop de requêtes » (SS §6).
    [HttpPost("movements")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumBagRequestBytes)]
    public async Task<IActionResult> Move(BagMovementRequest request)
    {
        var result = await movementService.MoveAsync(
            SessionClaims.ReadUserId(User),
            request.SourceBagId,
            request.DestinationBagId,
            request.AmountInCents);

        if (result is BagMovementResult.Moved moved)
        {
            var movement = moved.Movement;
            var response = new BagMovementResponse(
                movement.Id,
                movement.MovedAt,
                movement.AmountInCents,
                movement.SourceBagId,
                movement.DestinationBagId);

            return StatusCode(StatusCodes.Status201Created, response);
        }

        if (result is BagMovementResult.NoBag)
        {
            return AnswerFieldRefused(nameof(BagMovementRequest.DestinationBagId), ErrorMessages.MovementWithoutBag);
        }

        if (result is BagMovementResult.SameBag)
        {
            return AnswerFieldRefused(nameof(BagMovementRequest.DestinationBagId), ErrorMessages.MovementWithSameBag);
        }

        if (result is BagMovementResult.AmountBreaksRules)
        {
            return AnswerFieldRefused(nameof(BagMovementRequest.AmountInCents), ErrorMessages.MovementAmountBreaksRules);
        }

        if (result is BagMovementResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Bag, request.SourceBagId);
            await RecordForeignAccessAsync(OwnedResource.Bag, request.DestinationBagId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is BagMovementResult.TooManyMovements)
        {
            return Problem(statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (result is BagMovementResult.SourceBagTooLow)
        {
            return AnswerFieldRefused(nameof(BagMovementRequest.AmountInCents), ErrorMessages.SourceBagTooLow);
        }

        if (result is BagMovementResult.AvailableMoneyTooLow)
        {
            return AnswerFieldRefused(nameof(BagMovementRequest.AmountInCents), ErrorMessages.AvailableMoneyTooLow);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.BagsChangedMeanwhile);
    }

    // GET api/bags/{id}/movements : l'historique des mouvements du sac, les plus récents d'abord, 50 par page.
    // Ce sont des données financières : aucun navigateur ni intermédiaire ne doit les garder.
    [HttpGet("{bagId:guid}/movements")]
    [EnableRateLimiting(RateLimitingConfiguration.CostlyReadPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> History(Guid bagId, [FromQuery] BagHistoryRequest request)
    {
        var page = request.Page ?? 1;
        var result = await historyService.ReadAsync(SessionClaims.ReadUserId(User), bagId, page);

        if (result is BagHistoryResult.Found found)
        {
            var items = new List<BagHistoryItemResponse>();

            foreach (var item in found.Page.Items)
            {
                items.Add(new BagHistoryItemResponse(item.Id, item.MovedAt, item.AmountInCents, item.OtherBagId, item.OtherBagName));
            }

            return Ok(new BagHistoryPageResponse(items, page, BagHistoryService.PageSize, found.Page.TotalCount));
        }

        if (result is BagHistoryResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Bag, bagId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        return AnswerFieldRefused(nameof(BagHistoryRequest.Page), ErrorMessages.BagHistoryPageBreaksRules);
    }

    // Note au journal si la ressource visée est à un autre utilisateur (SS §11). La réponse reste la même dans tous les cas.
    private async Task RecordForeignAccessAsync(OwnedResource resource, Guid? resourceId)
    {
        await foreignAccessJournal.RecordIfForeignAsync(
            SessionClaims.ReadUserId(User),
            HttpContext.Connection.RemoteIpAddress,
            resource,
            resourceId);
    }

    // Donne le message d'un champ refusé.
    private ActionResult AnswerFieldRefused(string field, string message)
    {
        ModelState.AddModelError(field, message);

        return ValidationProblem();
    }

    // Donne le message d'un objectif refusé, sur le champ qui ne va pas. Une date sans montant est refusée sur le montant :
    // c'est lui qui manque.
    private ActionResult AnswerGoalRefused(BagGoalProblem problem)
    {
        if (problem == BagGoalProblem.AmountBreaksRules)
        {
            return AnswerFieldRefused(nameof(BagGoalRequest.TargetAmountInCents), ErrorMessages.BagGoalAmountBreaksRules);
        }

        if (problem == BagGoalProblem.DateWithoutAmount)
        {
            return AnswerFieldRefused(nameof(BagGoalRequest.TargetAmountInCents), ErrorMessages.BagGoalDateWithoutAmount);
        }

        return AnswerFieldRefused(nameof(BagGoalRequest.TargetDate), ErrorMessages.BagGoalDateTooEarly);
    }

    // Traduit un sac en réponse de l'API.
    private static BagResponse CreateResponse(BagView view)
    {
        return new BagResponse(
            view.Id,
            view.Name,
            view.BalanceInCents,
            view.TargetAmountInCents,
            view.TargetDate,
            view.ProgressPercent);
    }
}
