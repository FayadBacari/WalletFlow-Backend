namespace WalletFlow.Domain.AccountsAndCategories;

// Choisit la catégorie d'une opération qui arrive de la banque (GR Dépenses) : d'abord la règle de l'utilisateur
// pour ce libellé, qui l'emporte sur une règle commune, puis la règle commune, qui vise une catégorie par son nom
// sans tenir compte des majuscules, comme l'unicité des noms (DD §6.1 et §6.2), enfin « Autres ».
public static class CategoryChoice
{
    // Renvoie l'identifiant de la catégorie choisie parmi celles de l'utilisateur, ou null s'il n'a plus de « Autres » :
    // son compte vient d'être effacé.
    public static Guid? ForArrivingTransaction(string label, List<CategorizationRule> userRules, List<Category> userCategories)
    {
        var otherCategory = userCategories.FirstOrDefault(category => category.IsOtherCategory);

        if (otherCategory == null)
        {
            return null;
        }

        var simpleLabel = TransactionLabels.Simplify(label);
        var userRule = userRules.FirstOrDefault(rule => simpleLabel.Length > 0 && rule.Label == simpleLabel);

        if (userRule != null && userCategories.Any(category => category.Id == userRule.CategoryId))
        {
            return userRule.CategoryId;
        }

        var commonCategoryName = CommonCategorizationRules.FindCategoryName(label);

        if (commonCategoryName != null)
        {
            var namedCategory = userCategories.FirstOrDefault(category =>
                category.Name.ToLowerInvariant() == commonCategoryName.ToLowerInvariant());

            if (namedCategory != null)
            {
                return namedCategory.Id;
            }
        }

        return otherCategory.Id;
    }
}
