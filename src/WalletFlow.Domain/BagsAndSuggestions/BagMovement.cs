namespace WalletFlow.Domain.BagsAndSuggestions;

// Déplacement d'argent vers un sac, d'un sac à un autre, ou d'un sac vers l'argent disponible.
// Correspond à la table mouvement_sac du MPD (DD §7.2). Les dates sont en UTC.
public class BagMovement
{
    // id_mouvement
    public Guid Id { get; set; } = Guid.NewGuid();

    // montant_mouvement : en centimes, toujours positif.
    public int AmountInCents { get; set; }

    // date_mouvement
    public DateTime MovedAt { get; set; } = DateTime.UtcNow;

    // id_sac_depart : vide quand l'argent vient de l'argent disponible.
    public Guid? SourceBagId { get; set; }

    // id_sac_arrivee : vide quand l'argent retourne dans l'argent disponible.
    public Guid? DestinationBagId { get; set; }

    // id_suggestion : la suggestion acceptée qui a créé ce mouvement, s'il y en a une.
    public Guid? SavingsSuggestionId { get; set; }
}
