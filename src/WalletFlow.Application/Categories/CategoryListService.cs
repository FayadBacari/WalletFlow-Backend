using System.Globalization;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// Cas d'utilisation : l'utilisateur connecté voit ses catégories, avec leur budget (GR Dépenses).
public class CategoryListService
{
    // L'ordre alphabétique français : « Épargne » se range avec les E, pas après le Z.
    private static readonly CompareInfo FrenchOrder = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

    private readonly ICategoryRepository categoryRepository;

    // Reçoit l'accès aux catégories.
    public CategoryListService(ICategoryRepository categoryRepository)
    {
        this.categoryRepository = categoryRepository;
    }

    // Donne les catégories de l'utilisateur dans l'ordre alphabétique, avec « Autres » à la fin.
    public async Task<List<Category>> ListAsync(Guid userId)
    {
        var categories = await categoryRepository.FindAllAsync(userId);

        categories.Sort(CompareForList);

        return categories;
    }

    // Range « Autres » après toutes les autres catégories, et les autres par leur nom.
    private static int CompareForList(Category first, Category second)
    {
        if (first.IsOtherCategory != second.IsOtherCategory)
        {
            return first.IsOtherCategory ? 1 : -1;
        }

        return FrenchOrder.Compare(first.Name, second.Name);
    }
}
