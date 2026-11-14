using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Security;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Retrouve dans la base PostgreSQL le propriétaire d'une ressource, sans filtrer sur l'utilisateur de la session.
// Seul le journal des accès refusés s'en sert (SS §11) : aucun cas d'utilisation ne donne un accès avec ce dépôt.
// Rien n'est suivi par EF Core : ces lectures n'enregistrent jamais rien.
public class ResourceOwnershipRepository : IResourceOwnershipRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public ResourceOwnershipRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Lit l'utilisateur de la ressource. Un compte bancaire et une opération appartiennent à l'utilisateur de leur connexion.
    public async Task<Guid?> FindOwnerAsync(OwnedResource resource, Guid resourceId)
    {
        switch (resource)
        {
            case OwnedResource.Category:
                return await databaseContext.Categories.AsNoTracking()
                    .Where(category => category.Id == resourceId)
                    .Select(category => (Guid?)category.UserId)
                    .SingleOrDefaultAsync();
            case OwnedResource.Bag:
                return await databaseContext.Bags.AsNoTracking()
                    .Where(bag => bag.Id == resourceId)
                    .Select(bag => (Guid?)bag.UserId)
                    .SingleOrDefaultAsync();
            case OwnedResource.BankConnection:
                return await databaseContext.BankConnections.AsNoTracking()
                    .Where(connection => connection.Id == resourceId)
                    .Select(connection => (Guid?)connection.UserId)
                    .SingleOrDefaultAsync();
            case OwnedResource.BankAccount:
                return await databaseContext.BankAccounts.AsNoTracking()
                    .Where(account => account.Id == resourceId)
                    .Join(
                        databaseContext.BankConnections,
                        account => account.BankConnectionId,
                        connection => connection.Id,
                        (account, connection) => (Guid?)connection.UserId)
                    .SingleOrDefaultAsync();
            default:
                return await databaseContext.BankTransactions.AsNoTracking()
                    .Where(transaction => transaction.Id == resourceId)
                    .Join(
                        databaseContext.BankAccounts,
                        transaction => transaction.BankAccountId,
                        account => account.Id,
                        (transaction, account) => account.BankConnectionId)
                    .Join(
                        databaseContext.BankConnections,
                        connectionId => connectionId,
                        connection => connection.Id,
                        (connectionId, connection) => (Guid?)connection.UserId)
                    .SingleOrDefaultAsync();
        }
    }
}
