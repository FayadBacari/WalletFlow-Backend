using System.Globalization;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// L'ordre dans lequel l'utilisateur voit ses catégories : alphabétique en français, avec « Autres » à la fin.
public static class CategoryOrder
{
    // L'ordre alphabétique français : « Épargne » se range avec les E, pas après le Z.
    private static readonly CompareInfo FrenchOrder = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

    // Range « Autres » après toutes les autres catégories, et les autres par leur nom.
    public static int Compare(Category first, Category second)
    {
        if (first.IsOtherCategory != second.IsOtherCategory)
        {
            return first.IsOtherCategory ? 1 : -1;
        }

        return FrenchOrder.Compare(first.Name, second.Name);
    }
}
