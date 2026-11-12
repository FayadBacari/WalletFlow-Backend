using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les connexions bancaires et leurs comptes dans la base PostgreSQL.
// Les recherches d'un utilisateur filtrent sur l'utilisateur de la session (SS §4).
// Les objets lus ici sont suivis par EF Core : l'unité de travail enregistre ce que leurs règles ont changé (voir IDatabaseUnitOfWork).
public class BankConnectionRepository : IBankConnectionRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public BankConnectionRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Donne les connexions de l'utilisateur.
    public async Task<List<BankConnection>> FindConnectionsAsync(Guid userId)
    {
        return await databaseContext.BankConnections
            .Where(connection => connection.UserId == userId)
            .ToListAsync();
    }

    // Cherche la connexion par son identifiant et par son utilisateur : celle d'un autre n'est jamais trouvée.
    public async Task<BankConnection?> FindConnectionAsync(Guid userId, Guid connectionId)
    {
        return await databaseContext.BankConnections.SingleOrDefaultAsync(connection =>
            connection.Id == connectionId
            && connection.UserId == userId);
    }

    // Cherche une connexion par son seul identifiant. Réservé à la tâche de fond, et aux connexions déjà trouvées
    // parmi celles de l'utilisateur.
    public async Task<BankConnection?> FindConnectionByIdAsync(Guid connectionId)
    {
        return await databaseContext.BankConnections.SingleOrDefaultAsync(connection =>
            connection.Id == connectionId);
    }

    // Relit l'identifiant chez le prestataire sans rien suivre : la valeur vient toujours de la base, jamais d'une lecture précédente.
    public async Task<string?> ReadExternalIdAsync(Guid userId, Guid connectionId)
    {
        return await databaseContext.BankConnections
            .AsNoTracking()
            .Where(connection => connection.Id == connectionId && connection.UserId == userId)
            .Select(connection => connection.ExternalConnectionId)
            .SingleOrDefaultAsync();
    }

    // Donne les comptes de ces connexions.
    public async Task<List<BankAccount>> FindAccountsAsync(List<Guid> connectionIds)
    {
        return await databaseContext.BankAccounts
            .Where(account => connectionIds.Contains(account.BankConnectionId))
            .ToListAsync();
    }

    // Donne les connexions qui ne sont pas expirées, de tous les utilisateurs : la tâche de fond les met à jour.
    public async Task<List<Guid>> FindConnectionIdsToUpdateAsync()
    {
        return await databaseContext.BankConnections
            .Where(connection => connection.Status != BankConnectionStatus.Expired)
            .Select(connection => connection.Id)
            .ToListAsync();
    }

    // Cherche ce compte dans les autres connexions de l'utilisateur, par son identifiant stable.
    public async Task<bool> IsAccountConnectedElsewhereAsync(Guid userId, string stableId, Guid? exceptConnectionId)
    {
        var excludedConnectionId = exceptConnectionId ?? Guid.Empty;

        return await databaseContext.BankAccounts
            .Join(
                databaseContext.BankConnections,
                account => account.BankConnectionId,
                connection => connection.Id,
                (account, connection) => new { account.ExternalAccountId, connection.UserId, ConnectionId = connection.Id })
            .AnyAsync(row =>
                row.UserId == userId
                && row.ExternalAccountId == stableId
                && row.ConnectionId != excludedConnectionId);
    }

    // Garde de côté la nouvelle connexion : elle ne sera écrite qu'avec les autres changements.
    public void PrepareConnection(BankConnection connection)
    {
        databaseContext.BankConnections.Add(connection);
    }

    // Garde de côté le nouveau compte : il ne sera écrit qu'avec les autres changements.
    public void PrepareAccount(BankAccount account)
    {
        databaseContext.BankAccounts.Add(account);
    }

    // Une seule instruction : la connexion part avec ses comptes et leurs transactions (ON DELETE CASCADE).
    // Cette écriture est immédiate : elle refuse de partir si des changements attendent l'unité de travail.
    // Un renouvellement simultané verrouille le compte puis la connexion, cette suppression la connexion puis le compte :
    // si PostgreSQL arrête cette suppression pour débloquer les deux, rien n'est supprimé, et le cas d'utilisation recommence.
    public async Task<bool> DeleteAsync(Guid userId, Guid connectionId, string externalConnectionId)
    {
        databaseContext.ThrowIfChangesArePending();

        try
        {
            var deletedCount = await databaseContext.BankConnections
                .Where(connection =>
                    connection.Id == connectionId
                    && connection.UserId == userId
                    && connection.ExternalConnectionId == externalConnectionId)
                .ExecuteDeleteAsync();

            return deletedCount > 0;
        }
        catch (Exception exception) when (PostgresErrors.IsDeadlock(exception))
        {
            return false;
        }
    }
}
