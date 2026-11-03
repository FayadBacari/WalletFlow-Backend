namespace WalletFlow.Domain.AccountsAndCategories;

// Catégorie de dépenses ou de revenus d'un utilisateur.
// Correspond à la table categorie du MPD (DD §6.1).
public class Category
{
    // id_categorie
    public Guid Id { get; set; } = Guid.NewGuid();

    // nom_categorie
    public string Name { get; set; } = string.Empty;

    // budget_mensuel : en centimes, facultatif.
    public int? MonthlyBudgetInCents { get; set; }

    // est_categorie_autres : vrai seulement pour « Autres », qui ne se renomme ni ne se supprime.
    public bool IsOtherCategory { get; set; } = false;

    // id_utilisateur
    public Guid UserId { get; set; }
}
