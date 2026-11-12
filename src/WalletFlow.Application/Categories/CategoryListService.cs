using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// Cas d'utilisation : l'utilisateur connecté voit ses catégories, avec leur budget (GR Dépenses).
public class CategoryListService
{
    private readonly ICategoryRepository categoryRepository;

    // Reçoit l'accès aux catégories.
    public CategoryListService(ICategoryRepository categoryRepository)
    {
        this.categoryRepository = categoryRepository;
    }

    // Donne les catégories de l'utilisateur dans l'ordre alphabétique, avec « Autres » à la fin (voir CategoryOrder).
    public async Task<List<Category>> ListAsync(Guid userId)
    {
        var categories = await categoryRepository.FindAllAsync(userId);

        categories.Sort(CategoryOrder.Compare);

        return categories;
    }
}
