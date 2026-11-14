using Microsoft.AspNetCore.Mvc;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Categories;
using WalletFlow.Application.Security;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Api.Controllers;

// Catégories de l'utilisateur connecté : la liste, la création, le nom, le budget et la suppression (GR Dépenses).
// Ces routes n'ont aucun attribut de sécurité : toute route demande par défaut une session et le jeton anti-CSRF (voir Program.cs).
// La catégorie d'un autre utilisateur répond 404, comme une adresse qui n'existe pas : rien ne dit qu'elle existe (SS §4).
[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    public const string CategoryDeletedMessage = "La catégorie est supprimée. Ses transactions sont maintenant dans « Autres ».";

    private readonly CategoryListService listService;
    private readonly CategoryCreationService creationService;
    private readonly CategoryRenameService renameService;
    private readonly CategoryBudgetService budgetService;
    private readonly CategoryDeletionService deletionService;
    private readonly ForeignAccessJournalService foreignAccessJournal;

    // Reçoit les cas d'utilisation des catégories.
    public CategoriesController(
        CategoryListService listService,
        CategoryCreationService creationService,
        CategoryRenameService renameService,
        CategoryBudgetService budgetService,
        CategoryDeletionService deletionService,
        ForeignAccessJournalService foreignAccessJournal)
    {
        this.listService = listService;
        this.creationService = creationService;
        this.renameService = renameService;
        this.budgetService = budgetService;
        this.deletionService = deletionService;
        this.foreignAccessJournal = foreignAccessJournal;
    }

    // GET api/categories : les catégories de l'utilisateur avec leur budget, dans l'ordre alphabétique, « Autres » à la fin.
    // Ce sont des données financières : aucun navigateur ni intermédiaire ne doit les garder.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List()
    {
        var categories = await listService.ListAsync(SessionClaims.ReadUserId(User));
        var responses = new List<CategoryResponse>();

        foreach (var category in categories)
        {
            responses.Add(CreateResponse(category));
        }

        return Ok(responses);
    }

    // POST api/categories : crée une catégorie avec ce nom, sans budget.
    [HttpPost]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumCategoryRequestBytes)]
    public async Task<IActionResult> Create(CategoryNameRequest request)
    {
        var result = await creationService.CreateAsync(SessionClaims.ReadUserId(User), request.Name);

        if (result is CategoryCreationResult.Created created)
        {
            return StatusCode(StatusCodes.Status201Created, CreateResponse(created.Category));
        }

        if (result is CategoryCreationResult.RefusedByRules)
        {
            return AnswerNameRefused(ErrorMessages.CategoryNameBreaksRules);
        }

        if (result is CategoryCreationResult.NameAlreadyUsed)
        {
            return AnswerNameRefused(ErrorMessages.CategoryNameAlreadyUsed);
        }

        if (result is CategoryCreationResult.TooManyCategories)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.TooManyCategories);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.CategoriesChangedMeanwhile);
    }

    // PUT api/categories/{id}/name : donne un nouveau nom à la catégorie. « Autres » ne se renomme pas.
    [HttpPut("{categoryId:guid}/name")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumCategoryRequestBytes)]
    public async Task<IActionResult> Rename(Guid categoryId, CategoryNameRequest request)
    {
        var result = await renameService.RenameAsync(SessionClaims.ReadUserId(User), categoryId, request.Name);

        if (result is CategoryRenameResult.Renamed renamed)
        {
            return Ok(CreateResponse(renamed.Category));
        }

        if (result is CategoryRenameResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Category, categoryId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is CategoryRenameResult.OtherCategoryIsFixed)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.OtherCategoryIsFixed);
        }

        if (result is CategoryRenameResult.RefusedByRules)
        {
            return AnswerNameRefused(ErrorMessages.CategoryNameBreaksRules);
        }

        if (result is CategoryRenameResult.NameAlreadyUsed)
        {
            return AnswerNameRefused(ErrorMessages.CategoryNameAlreadyUsed);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.CategoriesChangedMeanwhile);
    }

    // PUT api/categories/{id}/budget : fixe le budget mensuel en centimes, ou l'enlève avec null.
    [HttpPut("{categoryId:guid}/budget")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumCategoryRequestBytes)]
    public async Task<IActionResult> ReplaceBudget(Guid categoryId, CategoryBudgetRequest request)
    {
        var result = await budgetService.ReplaceAsync(SessionClaims.ReadUserId(User), categoryId, request.MonthlyBudgetInCents);

        if (result is CategoryBudgetResult.Changed changed)
        {
            return Ok(CreateResponse(changed.Category));
        }

        if (result is CategoryBudgetResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Category, categoryId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        if (result is CategoryBudgetResult.RefusedByRules)
        {
            ModelState.AddModelError(nameof(CategoryBudgetRequest.MonthlyBudgetInCents), ErrorMessages.CategoryBudgetBreaksRules);
            return ValidationProblem();
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.CategoriesChangedMeanwhile);
    }

    // DELETE api/categories/{id} : supprime la catégorie, et range ses transactions dans « Autres ». « Autres » ne se supprime pas.
    // Cette route ne lit aucun corps : la limite empêche seulement qu'on lui en envoie un énorme.
    [HttpDelete("{categoryId:guid}")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumCategoryRequestBytes)]
    public async Task<IActionResult> Delete(Guid categoryId)
    {
        var result = await deletionService.DeleteAsync(SessionClaims.ReadUserId(User), categoryId);

        if (result is CategoryDeletionResult.Deleted)
        {
            return Ok(new MessageResponse(CategoryDeletedMessage));
        }

        if (result is CategoryDeletionResult.NotFound)
        {
            await RecordForeignAccessAsync(OwnedResource.Category, categoryId);
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.OtherCategoryIsFixed);
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

    // Donne le message d'un nom refusé, sur le champ Name.
    private ActionResult AnswerNameRefused(string message)
    {
        ModelState.AddModelError(nameof(CategoryNameRequest.Name), message);

        return ValidationProblem();
    }

    // Traduit une catégorie en réponse de l'API.
    private static CategoryResponse CreateResponse(Category category)
    {
        return new CategoryResponse(category.Id, category.Name, category.MonthlyBudgetInCents, category.IsOtherCategory);
    }
}
