namespace WalletFlow.Domain.AccountsAndCategories;

// Les 8 catégories créées pour chaque nouvel utilisateur (GR Dépenses, DD §6.1).
public static class DefaultCategories
{
    // Crée les 8 catégories par défaut d'un nouvel utilisateur.
    public static List<Category> CreateTheEightDefaultCategoriesForNewUser(Guid identifierOfTheNewlyRegisteredUser)
    {
        var namesOfTheOrdinaryDefaultCategories = new List<string>
        {
            "Logement", "Alimentation", "Transport", "Loisirs", "Santé", "Shopping", "Revenus"
        };
        var defaultCategoriesOfTheNewUser = new List<Category>();

        foreach (var nameOfTheDefaultCategoryToCreate in namesOfTheOrdinaryDefaultCategories)
        {
            defaultCategoriesOfTheNewUser.Add(new Category
            {
                Name = nameOfTheDefaultCategoryToCreate,
                UserId = identifierOfTheNewlyRegisteredUser
            });
        }

        // « Autres » reçoit les transactions sans catégorie : elle ne se renomme ni ne se supprime.
        defaultCategoriesOfTheNewUser.Add(new Category
        {
            Name = "Autres",
            IsOtherCategory = true,
            UserId = identifierOfTheNewlyRegisteredUser
        });

        return defaultCategoriesOfTheNewUser;
    }
}
