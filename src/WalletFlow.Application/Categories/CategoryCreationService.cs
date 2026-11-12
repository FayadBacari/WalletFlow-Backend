using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// Cas d'utilisation : l'utilisateur connecté crée une catégorie (GR Dépenses), dans la limite de 100 (SS §6).
public class CategoryCreationService
{
    private readonly ICategoryRepository categoryRepository;
    private readonly DatabaseChangesSavingService savingService;

    // Reçoit l'accès aux catégories et l'enregistrement avec réessai.
    public CategoryCreationService(ICategoryRepository categoryRepository, DatabaseChangesSavingService savingService)
    {
        this.categoryRepository = categoryRepository;
        this.savingService = savingService;
    }

    // Crée une catégorie avec le nom tapé, s'il respecte les règles et si l'utilisateur ne l'utilise pas déjà.
    public async Task<CategoryCreationResult> CreateAsync(Guid userId, string? typedName)
    {
        var name = CategoryRules.NormalizeName(typedName);

        if (!CategoryRules.IsNameValid(name))
        {
            return new CategoryCreationResult.RefusedByRules();
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareNewCategoryAsync(userId, name),
            new CategoryCreationResult.NotSaved());
    }

    // Un passage : compte les catégories, vérifie le nom, puis prépare la nouvelle catégorie.
    // « Autres » est réécrite en même temps : de deux créations simultanées, la seconde échoue et recompte.
    // Deux créations ne peuvent donc pas passer ensemble le contrôle du nombre maximum.
    private async Task<PreparedChanges<CategoryCreationResult>> PrepareNewCategoryAsync(Guid userId, string name)
    {
        var otherCategory = await categoryRepository.FindOtherCategoryAsync(userId);

        if (otherCategory == null)
        {
            return PreparedChanges<CategoryCreationResult>.NothingToSave(new CategoryCreationResult.NotSaved());
        }

        if (await categoryRepository.CountAsync(userId) >= CategoryRules.MaximumCountPerUser)
        {
            return PreparedChanges<CategoryCreationResult>.NothingToSave(new CategoryCreationResult.TooManyCategories());
        }

        if (await categoryRepository.IsNameUsedAsync(userId, name, null))
        {
            return PreparedChanges<CategoryCreationResult>.NothingToSave(new CategoryCreationResult.NameAlreadyUsed());
        }

        var newCategory = new Category
        {
            Name = name,
            UserId = userId
        };
        categoryRepository.PrepareCategory(newCategory);
        categoryRepository.PrepareUnchangedCheck(otherCategory);

        return PreparedChanges<CategoryCreationResult>.SaveThenReturn(
            new CategoryCreationResult.Created { Category = newCategory });
    }
}
