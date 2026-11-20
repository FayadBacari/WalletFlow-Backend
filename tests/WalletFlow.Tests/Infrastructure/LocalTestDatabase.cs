using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;

namespace WalletFlow.Tests.Infrastructure;

// Donne aux tests un accès à la vraie base PostgreSQL locale des tests, avec l'utilisateur de l'API.
public static class LocalTestDatabase
{
    // Une autre machine, ou une intégration continue, peut désigner sa propre base avec cette variable d'environnement.
    public const string DatabaseVariableName = "WALLETFLOW_TEST_DATABASE";

    // Le nom de toute base des tests finit par ce mot. Les tests écrivent et effacent des lignes : ils ne touchent donc jamais
    // la base de développement « walletflow », ni une base de production, même si la variable les y envoie par erreur.
    public const string NameEnding = "_tests";

    // Sans la variable : la base locale des tests, dont le mode « trust » ne demande pas de mot de passe (voir SS §7 pour la
    // production).
    public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=walletflow_tests;Username=walletflow_app";

    // Crée un accès à la base des tests. Un intercepteur peut agir entre deux commandes, comme une autre requête au même instant.
    public static WalletFlowDatabaseContext CreateDatabaseContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<WalletFlowDatabaseContext>()
            .UseNpgsql(ReadConnectionString())
            .AddInterceptors(interceptors)
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

    // Crée la préparation des alertes, sur les vraies alertes et la vraie file d'e-mails de cet accès à la base.
    public static AlertPreparation CreateAlertPreparation(WalletFlowDatabaseContext databaseContext)
    {
        return new AlertPreparation(new AlertRepository(databaseContext), new EmailRequestRepository(databaseContext, new EmailRequestWakeUp()));
    }

    // Crée la vérification des budgets, sur cet accès à la base.
    public static BudgetAlertService CreateBudgetAlerts(WalletFlowDatabaseContext databaseContext, TimeProvider clock)
    {
        return new BudgetAlertService(
            new CategoryRepository(databaseContext),
            new TransactionRepository(databaseContext),
            new AlertRepository(databaseContext),
            CreateAlertPreparation(databaseContext),
            CreateSavingService(databaseContext),
            clock);
    }

    // Crée l'alerte d'objectif des sacs, sur cet accès à la base.
    public static BagGoalAlerts CreateBagGoalAlerts(WalletFlowDatabaseContext databaseContext)
    {
        return new BagGoalAlerts(new BagRepository(databaseContext), new AlertRepository(databaseContext), CreateAlertPreparation(databaseContext));
    }

    // Enregistre un nouveau compte confirmé avec cette adresse, puis oublie les objets gardés en mémoire.
    public static async Task<User> SaveConfirmedAccountAsync(WalletFlowDatabaseContext databaseContext, string emailAddress)
    {
        var newAccount = new User { Email = emailAddress, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
        databaseContext.Users.Add(newAccount);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return newAccount;
    }

    // Une autre requête écrit pendant celle du test. Dans l'API, chaque requête a sa propre transaction : la version de la ligne (xmin) change.
    // Les tests travaillent dans une seule transaction : un point de sauvegarde donne à cette écriture son propre numéro, comme une autre requête.
    public static async Task WriteAsAnotherRequestAsync(WalletFlowDatabaseContext databaseContext, Func<Task> write)
    {
        await databaseContext.Database.ExecuteSqlRawAsync("SAVEPOINT autre_requete");
        await write();
        await databaseContext.Database.ExecuteSqlRawAsync("RELEASE SAVEPOINT autre_requete");
    }

    // Prend la base donnée par la variable d'environnement si elle existe, sinon la base locale des tests. Une base dont le nom
    // ne finit pas par « _tests » est refusée avant toute connexion.
    public static string ReadConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(DatabaseVariableName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DefaultConnectionString;
        }

        return RefuseIfNotTestDatabase(connectionString);
    }

    // Renvoie la chaîne de connexion si sa base est une base des tests, sinon refuse. Le nom est lu comme Npgsql le lit pour se
    // connecter : un synonyme (« DB ») ou une clé écrite deux fois ne trompent pas ce contrôle. Le refus ne recopie que le nom de
    // la base, jamais la chaîne, qui peut contenir un mot de passe.
    public static string RefuseIfNotTestDatabase(string connectionString)
    {
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;

        if (databaseName == null || !databaseName.EndsWith(NameEnding, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Les tests refusent la base « " + databaseName + " » : le nom d'une base des tests finit par « " + NameEnding
                + " », car les tests y écrivent et y effacent des lignes. Crée la base locale des tests (voir le README), ou donne "
                + "une base des tests dans " + DatabaseVariableName + ".");
        }

        return connectionString;
    }
}
