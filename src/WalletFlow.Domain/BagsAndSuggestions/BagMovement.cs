namespace WalletFlow.Domain.BagsAndSuggestions;

// Déplacement d'argent vers un sac, d'un sac à un autre, ou d'un sac vers l'argent disponible.
// Correspond à la table mouvement_sac du MPD (DD §7.2). Les dates sont en UTC.
public class BagMovement
{
    // id_mouvement
    public Guid Id { get; init; } = Guid.NewGuid();

    // montant_mouvement : en centimes, toujours positif.
    public int AmountInCents { get; init; }

    // date_mouvement : donnée par l'horloge de l'application, jamais par celle de la machine, pour que les tests la maîtrisent.
    public required DateTime MovedAt { get; init; }

    // id_sac_depart : vide quand l'argent vient de l'argent disponible.
    public Guid? SourceBagId { get; init; }

    // id_sac_arrivee : vide quand l'argent retourne dans l'argent disponible.
    public Guid? DestinationBagId { get; init; }

    // id_suggestion : la suggestion acceptée qui a créé ce mouvement, s'il y en a une.
    public Guid? SavingsSuggestionId { get; init; }
}
