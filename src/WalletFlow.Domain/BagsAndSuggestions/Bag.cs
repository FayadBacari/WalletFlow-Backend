namespace WalletFlow.Domain.BagsAndSuggestions;

// Sac : enveloppe virtuelle dans laquelle l'utilisateur répartit son argent.
// Correspond à la table sac du MPD (DD §7.1).
public class Bag
{
    // id_sac
    public Guid Id { get; init; } = Guid.NewGuid();

    // nom_sac
    public string Name { get; init; } = string.Empty;

    // montant_objectif : en centimes, facultatif.
    public int? TargetAmountInCents { get; init; }

    // date_objectif : facultative, seulement si un montant à atteindre est fixé.
    public DateOnly? TargetDate { get; init; }

    // id_utilisateur
    public Guid UserId { get; init; }
}
