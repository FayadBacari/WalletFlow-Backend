namespace WalletFlow.Domain.AccountsAndCategories;

// Règle créée par l'utilisateur pour classer les prochaines opérations de même libellé (GR Dépenses).
// Correspond à la table regle_classement du MPD (DD §6.2).
public class CategorizationRule
{
    private Guid categoryId;

    // id_regle_classement
    public Guid Id { get; init; } = Guid.NewGuid();

    // libelle_regle : la forme simple du libellé (voir TransactionLabels). La règle reconnaît les libellés
    // qui ont la même forme simple : « CB CARREFOUR 12/11 » et « CB CARREFOUR 03/12 » ont la même.
    public string Label { get; init; } = string.Empty;

    // id_categorie : la catégorie donnée aux prochaines opérations de ce libellé.
    public Guid CategoryId
    {
        get => categoryId;
        init => categoryId = value;
    }

    // Renvoie vrai si ce libellé peut être celui d'une règle : il est déjà sous forme simple, et il a au moins une lettre.
    public static bool IsLabelValid(string label)
    {
        return label.Length > 0
            && label.Length <= TransactionRules.MaximumLabelLength
            && label == TransactionLabels.Simplify(label);
    }

    // Donne une autre catégorie aux prochaines opérations de ce libellé : le nouveau choix de l'utilisateur remplace l'ancien.
    public void ChangeCategory(Guid newCategoryId)
    {
        if (newCategoryId == Guid.Empty)
        {
            throw new ArgumentException("Une règle vise toujours une catégorie.", nameof(newCategoryId));
        }

        categoryId = newCategoryId;
    }
}
