using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Security;
using WalletFlow.Application.Transactions;

namespace WalletFlow.Api.Controllers;

// Opérations bancaires de l'utilisateur connecté : la recherche, le changement de catégorie et la marque de transfert
// (GR Comptes et GR Dépenses). Ces routes n'ont aucun attribut de sécurité : toute route demande par défaut une session
// et le jeton anti-CSRF (voir Program.cs). L'opération ou la catégorie d'un autre utilisateur répond 404,
// comme une adresse qui n'existe pas : rien ne dit qu'elle existe (SS §4).
[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly TransactionSearchService searchService;
    private readonly TransactionCategoryChangeService categoryChangeService;
    private readonly TransactionTransferService transferService;
    private readonly ForeignAccessJournalService foreignAccessJournal;

    // Reçoit les cas d'utilisation des opérations.
    public TransactionsController(
        TransactionSearchService searchService,
        TransactionCategoryChangeService categoryChangeService,
        TransactionTransferService transferService,
        ForeignAccessJournalService foreignAccessJournal)
    {
        this.searchService = searchService;
        this.categoryChangeService = categoryChangeService;
        this.transferService = transferService;
        this.foreignAccessJournal = foreignAccessJournal;
    }

    // GET api/transactions : cherche par libellé, montant, dates, compte et catégorie ; les plus récentes d'abord, 50 par page.
    // Ce sont des données financières : aucun navigateur ni intermédiaire ne doit les garder.
    [HttpGet]
    [EnableRateLimiting(RateLimitingConfiguration.CostlyReadPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Search([FromQuery] TransactionSearchRequest request)
    {
        var page = request.Page ?? 1;
        var result = await searchService.SearchAsync(
            SessionClaims.ReadUserId(User),
            request.Text,
            request.AmountInCents,
            request.From,
            request.To,
            request.AccountId,
            request.CategoryId,
            page);

        if (result is TransactionSearchResult.Found found)
        {
            // Le compte ou la catégorie d'un autre ne filtre rien de visible, mais la tentative est notée.
            await RecordForeignAccessAsync(OwnedResource.BankAccount, request.AccountId);
            await RecordForeignAccessAsync(OwnedResource.Category, request.CategoryId);
            var items = found.Page.Items.Select(CreateResponse).ToList();
            return Ok(new TransactionPageResponse(items, page, TransactionSearchService.PageSize, found.Page.TotalCount));
        }

        if (result is TransactionSearchResult.TextTooLong)
        {
            return AnswerFieldRefused(nameof(TransactionSearchRequest.Text), ErrorMessages.TransactionSearchTextTooLong);
        }

        if (result is TransactionSearchResult.AmountBreaksRules)
        {
            return AnswerFieldRefused(nameof(TransactionSearchRequest.AmountInCents), ErrorMessages.TransactionSearchAmountBreaksRules);
        }

        if (result is TransactionSearchResult.DatesReversed)
        {
            return AnswerFieldRefused(nameof(TransactionSearchRequest.To), ErrorMessages.DatesReversed);
        }

        return AnswerFieldRefused(nameof(TransactionSearchRequest.Page), ErrorMessages.TransactionSearchPageBreaksRules);
    }

    // PUT api/transactions/{id}/category : range l'opération dans une autre catégorie, et, si l'utilisateur le demande,
    // les prochaines opérations qui ont le même libellé aussi. Les opérations déjà reçues ne changent pas.
    [HttpPut("{transactionId:guid}/category")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumTransactionRequestBytes)]
    public async Task<IActionResult> ChangeCategory(Guid transactionId, TransactionCategoryRequest request)
    {
        var result = await categoryChangeService.ChangeAsync(
            SessionClaims.ReadUserId(User),
            transactionId,
            request.CategoryId,
            request.ApplyToFutureTransactions);

        if (result is TransactionCategoryChangeResult.Changed changed)
        {
            return Ok(CreateResponse(changed.Transaction));
        }

        if (result is TransactionCategoryChangeResult.TransactionNotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Transaction, transactionId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is TransactionCategoryChangeResult.CategoryNotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Category, request.CategoryId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is TransactionCategoryChangeResult.LabelWithoutLetter)
        {
            return AnswerFieldRefused(nameof(TransactionCategoryRequest.ApplyToFutureTransactions), ErrorMessages.TransactionLabelWithoutLetter);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.TransactionsChangedMeanwhile);
    }

    // PUT api/transactions/{id}/transfer : marque l'opération comme transfert entre deux comptes de l'utilisateur,
    // ou retire cette marque. Un transfert n'est compté ni en dépense ni en revenu.
    [HttpPut("{transactionId:guid}/transfer")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumTransactionRequestBytes)]
    public async Task<IActionResult> MarkTransfer(Guid transactionId, TransactionTransferRequest request)
    {
        var result = await transferService.MarkAsync(SessionClaims.ReadUserId(User), transactionId, request.IsInternalTransfer);

        if (result is TransactionTransferResult.Marked marked)
        {
            return Ok(CreateResponse(marked.Transaction));
        }

        if (result is TransactionTransferResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Transaction, transactionId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.TransactionsChangedMeanwhile);
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

    // Traduit une opération en réponse de l'API.
    private static TransactionResponse CreateResponse(TransactionView view)
    {
        return new TransactionResponse(
            view.Id,
            view.Date,
            view.Label,
            view.AmountInCents,
            view.IsInternalTransfer,
            view.AccountId,
            view.AccountName,
            view.CategoryId,
            view.CategoryName);
    }
}
