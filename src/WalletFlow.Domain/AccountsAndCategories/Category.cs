namespace WalletFlow.Domain.AccountsAndCategories;

// Catégorie de dépenses ou de revenus d'un utilisateur.
// Correspond à la table categorie du MPD (DD §6.1).
public class Category
{
    private string name = string.Empty;
    private int? monthlyBudgetInCents = null;

    // id_categorie
    public Guid Id { get; init; } = Guid.NewGuid();

    // nom_categorie
    public string Name
    {
        get => name;
        init => name = value;
    }

    // budget_mensuel : en centimes, facultatif.
    public int? MonthlyBudgetInCents
    {
        get => monthlyBudgetInCents;
        init => monthlyBudgetInCents = value;
    }

    // est_categorie_autres : vrai seulement pour « Autres », qui ne se renomme ni ne se supprime.
    public bool IsOtherCategory { get; init; } = false;

    // id_utilisateur
    public Guid UserId { get; init; }

    // Donne un nouveau nom à la catégorie (GR Dépenses). « Autres » ne se renomme pas.
    // Le nom arrive déjà sous sa forme enregistrée : une autre forme est refusée (voir CategoryRules).
    public void Rename(string newName)
    {
        if (IsOtherCategory)
        {
            throw new InvalidOperationException("La catégorie « Autres » ne peut pas être renommée.");
        }

        if (newName != CategoryRules.NormalizeName(newName) || !CategoryRules.IsNameValid(newName))
        {
            throw new ArgumentException("Le nom n'a pas sa forme enregistrée, ou ne respecte pas les règles du dictionnaire de données.", nameof(newName));
        }

        name = newName;
    }

    // Fixe le budget mensuel de la catégorie, ou l'enlève avec null (GR Dépenses).
    public void ReplaceMonthlyBudget(int? newBudgetInCents)
    {
        if (!CategoryRules.IsMonthlyBudgetValid(newBudgetInCents))
        {
            throw new ArgumentOutOfRangeException(nameof(newBudgetInCents), "Un budget va de 1 à 1 000 000 000 centimes.");
        }

        monthlyBudgetInCents = newBudgetInCents;
    }
}
