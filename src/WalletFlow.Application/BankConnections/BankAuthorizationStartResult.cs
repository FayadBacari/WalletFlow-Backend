namespace WalletFlow.Application.BankConnections;

// Résultat du début d'une autorisation bancaire : toujours une seule des formes écrites dans ce fichier.
public abstract class BankAuthorizationStartResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BankAuthorizationStartResult()
    {
    }

    // L'autorisation est commencée : le site envoie l'utilisateur à cette adresse, la page de sa banque.
    public sealed class Started : BankAuthorizationStartResult
    {
        public required string BankAddress { get; init; }
    }

    // Le prestataire ne propose pas cette banque.
    public sealed class UnknownBank : BankAuthorizationStartResult
    {
    }

    // La connexion à renouveler n'existe pas, ou elle est à un autre utilisateur.
    public sealed class NotFound : BankAuthorizationStartResult
    {
    }

    // Le prestataire ne répond pas.
    public sealed class ProviderUnavailable : BankAuthorizationStartResult
    {
    }
}
