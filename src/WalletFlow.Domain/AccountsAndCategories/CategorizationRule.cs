namespace WalletFlow.Domain.AccountsAndCategories;

// Règle créée par l'utilisateur pour classer les prochaines transactions de même libellé.
// Correspond à la table regle_classement du MPD (DD §6.2).
public class CategorizationRule
{
    // id_regle_classement
    public Guid Id { get; set; } = Guid.NewGuid();

    // libelle_regle : la règle s'applique aux libellés strictement identiques.
    public string Label { get; set; } = string.Empty;

    // id_categorie : la catégorie donnée aux transactions de ce libellé.
    public Guid CategoryId { get; set; }
}
