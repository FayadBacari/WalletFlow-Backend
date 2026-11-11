namespace WalletFlow.Domain.AccountsAndCategories;

// Catégorie de dépenses ou de revenus d'un utilisateur.
// Correspond à la table categorie du MPD (DD §6.1).
public class Category
{
    // id_categorie
    public Guid Id { get; init; } = Guid.NewGuid();

    // nom_categorie
    public string Name { get; init; } = string.Empty;

    // budget_mensuel : en centimes, facultatif.
    public int? MonthlyBudgetInCents { get; init; }

    // est_categorie_autres : vrai seulement pour « Autres », qui ne se renomme ni ne se supprime.
    public bool IsOtherCategory { get; init; } = false;

    // id_utilisateur
    public Guid UserId { get; init; }
}
