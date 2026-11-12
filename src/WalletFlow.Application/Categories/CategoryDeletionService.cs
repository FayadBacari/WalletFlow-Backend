namespace WalletFlow.Application.Categories;

// Cas d'utilisation : l'utilisateur connecté supprime une de ses catégories, sauf « Autres » (GR Dépenses).
// Ses transactions passent dans « Autres », ses règles disparaissent, et ses suggestions restent sans catégorie (MPD §1.3).
public class CategoryDeletionService
{
    private readonly ICategoryRepository categoryRepository;

    // Reçoit l'accès aux catégories.
    public CategoryDeletionService(ICategoryRepository categoryRepository)
    {
        this.categoryRepository = categoryRepository;
    }

    // Supprime la catégorie si elle est à l'utilisateur et n'est pas « Autres ».
    public async Task<CategoryDeletionResult> DeleteAsync(Guid userId, Guid categoryId)
    {
        var category = await categoryRepository.FindAsync(userId, categoryId);

        if (category == null)
        {
            return new CategoryDeletionResult.NotFound();
        }

        if (category.IsOtherCategory)
        {
            return new CategoryDeletionResult.OtherCategoryIsFixed();
        }

        // Une autre requête peut l'avoir supprimée juste après la lecture : la suppression ne trouve alors plus rien.
        if (!await categoryRepository.DeleteAsync(userId, categoryId))
        {
            return new CategoryDeletionResult.NotFound();
        }

        return new CategoryDeletionResult.Deleted();
    }
}
