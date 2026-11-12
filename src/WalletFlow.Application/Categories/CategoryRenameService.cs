using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// Cas d'utilisation : l'utilisateur connecté renomme une de ses catégories, sauf « Autres » (GR Dépenses).
public class CategoryRenameService
{
    private readonly ICategoryRepository categoryRepository;
    private readonly DatabaseChangesSavingService savingService;

    // Reçoit l'accès aux catégories et l'enregistrement avec réessai.
    public CategoryRenameService(ICategoryRepository categoryRepository, DatabaseChangesSavingService savingService)
    {
        this.categoryRepository = categoryRepository;
        this.savingService = savingService;
    }

    // Donne le nom tapé à la catégorie, s'il respecte les règles et si aucune autre catégorie de l'utilisateur ne le porte.
    public async Task<CategoryRenameResult> RenameAsync(Guid userId, Guid categoryId, string? typedName)
    {
        var name = CategoryRules.NormalizeName(typedName);

        if (!CategoryRules.IsNameValid(name))
        {
            return new CategoryRenameResult.RefusedByRules();
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareNewNameAsync(userId, categoryId, name),
            new CategoryRenameResult.NotSaved());
    }

    // Un passage : lit la catégorie parmi celles de l'utilisateur, vérifie le nom, puis prépare le changement.
    private async Task<PreparedChanges<CategoryRenameResult>> PrepareNewNameAsync(Guid userId, Guid categoryId, string name)
    {
        var category = await categoryRepository.FindAsync(userId, categoryId);

        if (category == null)
        {
            return PreparedChanges<CategoryRenameResult>.NothingToSave(new CategoryRenameResult.NotFound());
        }

        if (category.IsOtherCategory)
        {
            return PreparedChanges<CategoryRenameResult>.NothingToSave(new CategoryRenameResult.OtherCategoryIsFixed());
        }

        if (await categoryRepository.IsNameUsedAsync(userId, name, categoryId))
        {
            return PreparedChanges<CategoryRenameResult>.NothingToSave(new CategoryRenameResult.NameAlreadyUsed());
        }

        category.Rename(name);

        return PreparedChanges<CategoryRenameResult>.SaveThenReturn(new CategoryRenameResult.Renamed { Category = category });
    }
}
