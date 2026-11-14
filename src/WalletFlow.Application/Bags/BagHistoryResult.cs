namespace WalletFlow.Application.Bags;

// Résultat de la lecture de l'historique d'un sac : toujours une seule des formes écrites dans ce fichier.
public abstract class BagHistoryResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BagHistoryResult()
    {
    }

    // La page demandée de l'historique.
    public sealed class Found : BagHistoryResult
    {
        public required BagHistoryPage Page { get; init; }
    }

    // Le numéro de page ne va pas de 1 au maximum.
    public sealed class PageBreaksRules : BagHistoryResult
    {
    }

    // Le sac n'existe pas, ou il est à un autre utilisateur.
    public sealed class NotFound : BagHistoryResult
    {
    }
}
