using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// Cas d'utilisation : l'utilisateur connecté fixe ou enlève le budget mensuel d'une de ses catégories (GR Dépenses).
// « Autres » peut aussi avoir un budget. Le nouveau budget est comparé tout de suite aux dépenses du mois : s'il est déjà
// atteint, son alerte part.
public class CategoryBudgetService
{
    private readonly ICategoryRepository categoryRepository;
    private readonly BudgetAlertService budgetAlerts;
    private readonly DatabaseChangesSavingService savingService;

    // Reçoit l'accès aux catégories, la vérification des budgets et l'enregistrement avec réessai.
    public CategoryBudgetService(
        ICategoryRepository categoryRepository,
        BudgetAlertService budgetAlerts,
        DatabaseChangesSavingService savingService)
    {
        this.categoryRepository = categoryRepository;
        this.budgetAlerts = budgetAlerts;
        this.savingService = savingService;
    }

    // Fixe le budget en centimes, ou l'enlève avec null.
    public async Task<CategoryBudgetResult> ReplaceAsync(Guid userId, Guid categoryId, int? monthlyBudgetInCents, CancellationToken cancellationToken = default)
    {
        if (!CategoryRules.IsMonthlyBudgetValid(monthlyBudgetInCents))
        {
            return new CategoryBudgetResult.RefusedByRules();
        }

        var result = await savingService.SaveWithRetriesAsync(
            () => PrepareNewBudgetAsync(userId, categoryId, monthlyBudgetInCents, cancellationToken),
            new CategoryBudgetResult.NotSaved(),
            cancellationToken);

        if (result is CategoryBudgetResult.Changed)
        {
            await budgetAlerts.CheckAsync(userId, cancellationToken);
        }

        return result;
    }

    // Un passage : lit la catégorie parmi celles de l'utilisateur, puis prépare le nouveau budget.
    private async Task<PreparedChanges<CategoryBudgetResult>> PrepareNewBudgetAsync(Guid userId, Guid categoryId, int? monthlyBudgetInCents, CancellationToken cancellationToken = default)
    {
        var category = await categoryRepository.FindAsync(userId, categoryId, cancellationToken);

        if (category == null)
        {
            return PreparedChanges<CategoryBudgetResult>.NothingToSave(new CategoryBudgetResult.NotFound());
        }

        category.ReplaceMonthlyBudget(monthlyBudgetInCents);

        return PreparedChanges<CategoryBudgetResult>.SaveThenReturn(new CategoryBudgetResult.Changed { Category = category });
    }
}
