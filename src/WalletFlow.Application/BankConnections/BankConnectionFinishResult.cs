namespace WalletFlow.Application.BankConnections;

// Résultat du retour de la banque : toujours une seule des formes écrites dans ce fichier.
public abstract class BankConnectionFinishResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BankConnectionFinishResult()
    {
    }

    // La banque est connectée. Les comptes laissés de côté sont nommés : autre devise que l'euro, solde illisible,
    // ou compte déjà présent dans une autre connexion.
    public sealed class Connected : BankConnectionFinishResult
    {
        public required Guid ConnectionId { get; init; }
        public required int AddedAccountCount { get; init; }
        public required List<string> SkippedAccountNames { get; init; }
    }

    // L'autorisation de la connexion est renouvelée : ses comptes restent, avec leurs transactions.
    public sealed class Renewed : BankConnectionFinishResult
    {
        public required Guid ConnectionId { get; init; }
        public required int AddedAccountCount { get; init; }
        public required List<string> SkippedAccountNames { get; init; }
    }

    // Le state est inconnu, expiré, déjà utilisé, ou lié à un autre utilisateur : il faut recommencer depuis WalletFlow.
    public sealed class LinkNoLongerValid : BankConnectionFinishResult
    {
    }

    // La banque n'a pas donné l'autorisation, ou son code ne vaut plus rien.
    public sealed class RefusedByBank : BankConnectionFinishResult
    {
    }

    // Aucun compte n'a pu être ajouté : tous sont dans une autre devise, illisibles, ou déjà connectés.
    public sealed class NoAccountAdded : BankConnectionFinishResult
    {
        public required List<string> SkippedAccountNames { get; init; }
    }

    // La connexion à renouveler a été supprimée entre-temps.
    public sealed class ConnectionGone : BankConnectionFinishResult
    {
    }

    // Le prestataire ou la banque ne répond pas : rien n'est gardé.
    public sealed class ProviderUnavailable : BankConnectionFinishResult
    {
    }

    // D'autres requêtes changent les connexions sans arrêt : rien n'est gardé.
    public sealed class NotSaved : BankConnectionFinishResult
    {
    }
}
