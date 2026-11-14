namespace WalletFlow.Domain.BagsAndSuggestions;

// Sac : enveloppe virtuelle dans laquelle l'utilisateur répartit son argent.
// Correspond à la table sac du MPD (DD §7.1).
public class Bag
{
    private string name = string.Empty;
    private int? targetAmountInCents = null;
    private DateOnly? targetDate = null;

    // id_sac
    public Guid Id { get; init; } = Guid.NewGuid();

    // nom_sac
    public string Name
    {
        get => name;
        init => name = value;
    }

    // montant_objectif : en centimes, facultatif.
    public int? TargetAmountInCents
    {
        get => targetAmountInCents;
        init => targetAmountInCents = value;
    }

    // date_objectif : facultative, seulement si un montant à atteindre est fixé.
    public DateOnly? TargetDate
    {
        get => targetDate;
        init => targetDate = value;
    }

    // id_utilisateur
    public Guid UserId { get; init; }

    // Donne un nouveau nom au sac (GR Sacs). Le nom arrive déjà sous sa forme enregistrée : une autre forme est refusée.
    public void Rename(string newName)
    {
        if (newName != BagRules.NormalizeName(newName) || !BagRules.IsNameValid(newName))
        {
            throw new ArgumentException("Le nom n'a pas sa forme enregistrée, ou ne respecte pas les règles du dictionnaire de données.", nameof(newName));
        }

        name = newName;
    }

    // Fixe l'objectif du sac, ou l'enlève avec deux null (GR Sacs). La date doit être après le jour de la saisie (DD §7.1).
    public void ReplaceGoal(int? newTargetAmountInCents, DateOnly? newTargetDate, DateOnly today)
    {
        if (BagRules.FindGoalProblem(newTargetAmountInCents, newTargetDate, today) != BagGoalProblem.None)
        {
            throw new ArgumentException("L'objectif ne respecte pas les règles du dictionnaire de données.", nameof(newTargetAmountInCents));
        }

        targetAmountInCents = newTargetAmountInCents;
        targetDate = newTargetDate;
    }
}
