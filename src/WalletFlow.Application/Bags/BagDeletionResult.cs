namespace WalletFlow.Application.Bags;

// Résultat de la suppression d'un sac : toujours une seule des formes écrites dans ce fichier.
public abstract class BagDeletionResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BagDeletionResult()
    {
    }

    // Le sac est supprimé, et son argent est revenu dans l'argent disponible.
    public sealed class Deleted : BagDeletionResult
    {
    }

    // Le sac n'existe pas ou plus, ou il est à un autre utilisateur.
    public sealed class NotFound : BagDeletionResult
    {
    }

    // D'autres requêtes changent les sacs sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : BagDeletionResult
    {
    }
}
