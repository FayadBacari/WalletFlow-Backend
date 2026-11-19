using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Persistence;

namespace WalletFlow.Infrastructure.Persistence;

// Dit si la base PostgreSQL répond, avec la connexion de l'API.
public class DatabaseHealth : IDatabaseHealth
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de cette requête.
    public DatabaseHealth(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Ouvre une connexion et la fait répondre. EF Core renvoie faux si la base est injoignable.
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        return await databaseContext.Database.CanConnectAsync(cancellationToken);
    }
}
