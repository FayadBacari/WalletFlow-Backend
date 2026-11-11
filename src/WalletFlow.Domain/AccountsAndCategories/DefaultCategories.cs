namespace WalletFlow.Domain.AccountsAndCategories;

// Les 8 catégories créées pour chaque nouvel utilisateur (GR Dépenses, DD §6.1).
public static class DefaultCategories
{
    // Crée les 8 catégories par défaut d'un nouvel utilisateur.
    public static List<Category> CreateForUser(Guid userId)
    {
        var ordinaryCategoryNames = new List<string>
        {
            "Logement", "Alimentation", "Transport", "Loisirs", "Santé", "Shopping", "Revenus"
        };
        var categories = new List<Category>();

        foreach (var name in ordinaryCategoryNames)
        {
            categories.Add(new Category
            {
                Name = name,
                UserId = userId
            });
        }

        // « Autres » reçoit les transactions sans catégorie : elle ne se renomme ni ne se supprime.
        categories.Add(new Category
        {
            Name = "Autres",
            IsOtherCategory = true,
            UserId = userId
        });

        return categories;
    }
}
