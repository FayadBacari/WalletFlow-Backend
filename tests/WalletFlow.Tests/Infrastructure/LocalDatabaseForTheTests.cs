using Microsoft.EntityFrameworkCore;
using WalletFlow.Infrastructure.Persistence;

namespace WalletFlow.Tests.Infrastructure;

// Donne aux tests un accès à la vraie base PostgreSQL locale, avec l'utilisateur de l'API.
public static class LocalDatabaseForTheTests
{
    // Le mode « trust » de la base locale ne demande pas de mot de passe (voir SS §7 pour la production).
    private const string ConnectionStringOfTheLocalDatabase = "Host=localhost;Port=5432;Database=walletflow;Username=walletflow_app";

    // Crée un accès à la base locale.
    public static WalletFlowDatabaseContext CreateDatabaseContextConnectedToLocalDatabase()
    {
        var optionsOfTheContextConnectedToLocalDatabase = new DbContextOptionsBuilder<WalletFlowDatabaseContext>()
            .UseNpgsql(ConnectionStringOfTheLocalDatabase)
            .Options;

        return new WalletFlowDatabaseContext(optionsOfTheContextConnectedToLocalDatabase);
    }
}
