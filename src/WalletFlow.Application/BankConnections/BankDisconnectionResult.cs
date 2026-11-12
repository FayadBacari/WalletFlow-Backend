namespace WalletFlow.Application.BankConnections;

// Résultat de la déconnexion d'une banque : toujours une seule des formes écrites dans ce fichier.
public abstract class BankDisconnectionResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BankDisconnectionResult()
    {
    }

    // L'autorisation est révoquée, et la connexion est supprimée avec ses comptes et leurs transactions.
    public sealed class Disconnected : BankDisconnectionResult
    {
    }

    // La connexion n'existe pas, ou plus, ou elle est à un autre utilisateur.
    public sealed class NotFound : BankDisconnectionResult
    {
    }

    // Le prestataire n'a pas confirmé la révocation : rien n'est supprimé, l'utilisateur réessaiera.
    public sealed class ProviderUnavailable : BankDisconnectionResult
    {
    }

    // D'autres requêtes changent la connexion sans arrêt : rien n'est supprimé, l'utilisateur réessaiera.
    public sealed class NotSaved : BankDisconnectionResult
    {
    }
}
