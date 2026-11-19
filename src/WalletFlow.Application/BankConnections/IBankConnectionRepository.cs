using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.BankConnections;

// Accès aux connexions bancaires et à leurs comptes. Implémenté dans la couche Infrastructure.
// Les recherches d'un utilisateur reçoivent l'utilisateur de la session : la connexion d'un autre n'est jamais trouvée (SS §4).
// Les objets lus sont suivis, et les méthodes « Prepare… » n'écrivent rien (voir IDatabaseUnitOfWork).
public interface IBankConnectionRepository
{
    // Donne les connexions de l'utilisateur.
    Task<List<BankConnection>> FindConnectionsAsync(Guid userId, CancellationToken cancellationToken = default);

    // Cherche cette connexion parmi celles de l'utilisateur. Renvoie null si elle n'existe pas, ou si elle est à un autre.
    Task<BankConnection?> FindConnectionAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default);

    // Cherche une connexion par son seul identifiant, pour la mettre à jour. Réservé à la tâche de fond,
    // et aux connexions d'abord trouvées parmi celles de l'utilisateur.
    Task<BankConnection?> FindConnectionByIdAsync(Guid connectionId, CancellationToken cancellationToken = default);

    // Lit à nouveau en base l'identifiant chez le prestataire de cette connexion de l'utilisateur, sans rien suivre.
    // Renvoie null si elle n'existe plus.
    Task<string?> ReadExternalIdAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default);

    // Donne les comptes de ces connexions.
    Task<List<BankAccount>> FindAccountsAsync(List<Guid> connectionIds, CancellationToken cancellationToken = default);

    // Donne les connexions qui ne sont pas expirées, sauf celles des comptes en suppression : la tâche de fond les met à jour.
    Task<List<Guid>> FindConnectionIdsToUpdateAsync(CancellationToken cancellationToken = default);

    // Donne les connexions pas encore expirées des comptes en suppression : leur autorisation reste à révoquer (SS §7). Rien
    // n'est suivi.
    Task<List<Guid>> FindConnectionIdsToRevokeAsync(CancellationToken cancellationToken = default);

    // Renvoie vrai si ce compte, reconnu par son identifiant stable, est déjà dans une autre connexion de l'utilisateur.
    Task<bool> IsAccountConnectedElsewhereAsync(Guid userId, string stableId, Guid? exceptConnectionId, CancellationToken cancellationToken = default);

    // Garde de côté une nouvelle connexion : elle ne sera enregistrée qu'avec les autres changements.
    void PrepareConnection(BankConnection connection);

    // Garde de côté un nouveau compte : il ne sera enregistré qu'avec les autres changements.
    void PrepareAccount(BankAccount account);

    // La connexion sera écrite même si rien n'y a changé : l'enregistrement échoue alors si une autre requête l'a écrite
    // depuis sa lecture. L'alerte d'une autorisation à renouveler et le renouvellement passent ainsi un par un.
    void PrepareUnchangedCheck(BankConnection connection);

    // Supprime la connexion de l'utilisateur, avec ses comptes et leurs transactions (GR Comptes), si son identifiant
    // chez le prestataire est toujours celui-ci. Écriture immédiate. Renvoie faux si rien n'a été supprimé.
    Task<bool> DeleteAsync(Guid userId, Guid connectionId, string externalConnectionId, CancellationToken cancellationToken = default);
}
