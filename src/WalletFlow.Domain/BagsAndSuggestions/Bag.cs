namespace WalletFlow.Domain.BagsAndSuggestions;

// Sac : enveloppe virtuelle dans laquelle l'utilisateur répartit son argent.
// Correspond à la table sac du MPD (DD §7.1).
public class Bag
{
    // id_sac
    public Guid Id { get; set; } = Guid.NewGuid();

    // nom_sac
    public string Name { get; set; } = string.Empty;

    // montant_objectif : en centimes, facultatif.
    public int? TargetAmountInCents { get; set; }

    // date_objectif : facultative, seulement si un montant à atteindre est fixé.
    public DateOnly? TargetDate { get; set; }

    // id_utilisateur
    public Guid UserId { get; set; }
}
