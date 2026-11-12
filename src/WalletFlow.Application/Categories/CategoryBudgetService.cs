using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// Cas d'utilisation : l'utilisateur connecté fixe ou enlève le budget mensuel d'une de ses catégories (GR Dépenses).
// « Autres » peut aussi avoir un budget.
public class CategoryBudgetService
{
    private readonly ICategoryRepository categoryRepository;
    private readonly DatabaseChangesSavingService savingService;

    // Reçoit l'accès aux catégories et l'enregistrement avec réessai.
    public CategoryBudgetService(ICategoryRepository categoryRepository, DatabaseChangesSavingService savingService)
    {
        this.categoryRepository = categoryRepository;
        this.savingService = savingService;
    }

    // Fixe le budget en centimes, ou l'enlève avec null.
    public async Task<CategoryBudgetResult> ReplaceAsync(Guid userId, Guid categoryId, int? monthlyBudgetInCents)
    {
        if (!CategoryRules.IsMonthlyBudgetValid(monthlyBudgetInCents))
        {
            return new CategoryBudgetResult.RefusedByRules();
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareNewBudgetAsync(userId, categoryId, monthlyBudgetInCents),
            new CategoryBudgetResult.NotSaved());
    }

    // Un passage : lit la catégorie parmi celles de l'utilisateur, puis prépare le nouveau budget.
    private async Task<PreparedChanges<CategoryBudgetResult>> PrepareNewBudgetAsync(Guid userId, Guid categoryId, int? monthlyBudgetInCents)
    {
        var category = await categoryRepository.FindAsync(userId, categoryId);

        if (category == null)
        {
            return PreparedChanges<CategoryBudgetResult>.NothingToSave(new CategoryBudgetResult.NotFound());
        }

        category.ReplaceMonthlyBudget(monthlyBudgetInCents);

        return PreparedChanges<CategoryBudgetResult>.SaveThenReturn(new CategoryBudgetResult.Changed { Category = category });
    }
}
