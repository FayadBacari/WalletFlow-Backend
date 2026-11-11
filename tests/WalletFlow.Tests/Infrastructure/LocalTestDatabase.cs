using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;

namespace WalletFlow.Tests.Infrastructure;

// Donne aux tests un accès à la vraie base PostgreSQL locale, avec l'utilisateur de l'API.
public static class LocalTestDatabase
{
    // Une autre machine, ou une intégration continue, peut désigner sa propre base avec cette variable d'environnement.
    public const string DatabaseVariableName = "WALLETFLOW_TEST_DATABASE";

    // Sans la variable : la base locale, dont le mode « trust » ne demande pas de mot de passe (voir SS §7 pour la production).
    private const string ConnectionString = "Host=localhost;Port=5432;Database=walletflow;Username=walletflow_app";

    // Crée un accès à la base des tests.
    public static WalletFlowDatabaseContext CreateDatabaseContext()
    {
        var options = new DbContextOptionsBuilder<WalletFlowDatabaseContext>()
            .UseNpgsql(ReadConnectionString())
            .Options;

        return new WalletFlowDatabaseContext(options);
    }

    // Enregistre ce qui a été préparé sur cet accès à la base, avec la vraie unité de travail.
    public static async Task<bool> TrySaveChangesAsync(WalletFlowDatabaseContext databaseContext)
    {
        return await new EntityFrameworkDatabaseUnitOfWork(databaseContext).TrySaveChangesAsync();
    }

    // Crée le service qui enregistre les changements, avec ses réessais, sur cet accès à la base.
    public static DatabaseChangesSavingService CreateSavingService(WalletFlowDatabaseContext databaseContext)
    {
        return new DatabaseChangesSavingService(new EntityFrameworkDatabaseUnitOfWork(databaseContext));
    }

    // Crée le service qui écrit dans le vrai journal de sécurité, sur cet accès à la base.
    public static SecurityJournalService CreateSecurityJournal(WalletFlowDatabaseContext databaseContext, TimeProvider clock)
    {
        return new SecurityJournalService(new SecurityEventRepository(databaseContext), clock);
    }

    // Une autre requête écrit pendant celle du test. Dans l'API, chaque requête a sa propre transaction : la version de la ligne (xmin) change.
    // Les tests travaillent dans une seule transaction : un point de sauvegarde donne à cette écriture son propre numéro, comme une autre requête.
    public static async Task WriteAsAnotherRequestAsync(WalletFlowDatabaseContext databaseContext, Func<Task> write)
    {
        await databaseContext.Database.ExecuteSqlRawAsync("SAVEPOINT autre_requete");
        await write();
        await databaseContext.Database.ExecuteSqlRawAsync("RELEASE SAVEPOINT autre_requete");
    }

    // Prend la base donnée par la variable d'environnement si elle existe, sinon la base locale.
    public static string ReadConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(DatabaseVariableName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return ConnectionString;
        }

        return connectionString;
    }
}
