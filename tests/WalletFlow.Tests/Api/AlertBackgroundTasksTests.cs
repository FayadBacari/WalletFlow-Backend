using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.Bags;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Suggestions;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Application;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Application.SuggestionServices;

namespace WalletFlow.Tests.Api;

// Vérifie les deux tâches de fond des notifications : le calcul des suggestions du mois (GR Sacs), puis l'alerte des
// autorisations bancaires à renouveler et l'effacement des vieilles alertes (GR Comptes, SS §7). Une panne ne les arrête pas.
public class AlertBackgroundTasksTests
{
    private readonly SuggestionServices services = new SuggestionServices();
    private readonly MemoryLoggerProvider loggerProvider = new MemoryLoggerProvider();

    // Branche sur les fausses bases tout ce dont les deux tâches ont besoin, comme Program.cs.
    private ServiceProvider CreateServiceProvider()
    {
        var bank = services.Bank;
        var collection = new ServiceCollection();
        collection.AddSingleton<IBagRepository>(services.Bags);
        collection.AddSingleton<ISuggestionRepository>(services.Suggestions);
        collection.AddSingleton<ITransactionRepository>(bank.Transactions);
        collection.AddSingleton<IBankConnectionRepository>(bank.Repository);
        collection.AddSingleton<IAlertRepository>(services.Alerts);
        collection.AddSingleton<IEmailRequestRepository>(services.EmailRequests);
        collection.AddSingleton<IDatabaseUnitOfWork>(new FakeDatabaseUnitOfWork(
            services.Bags, services.Suggestions, bank.Repository, bank.Transactions, services.Alerts, services.EmailRequests));
        collection.AddSingleton<TimeProvider>(bank.Clock);
        collection.AddScoped<DatabaseChangesSavingService>();
        collection.AddScoped<AlertPreparation>();
        collection.AddScoped<BankBalanceTotalService>();
        collection.AddScoped<SuggestionCalculationService>();
        collection.AddScoped<BankAuthorizationAlertService>();
        collection.AddScoped<AlertCleanupService>();

        return collection.BuildServiceProvider();
    }

    private SuggestionsBackgroundService CreateSuggestionsTask()
    {
        var serviceProvider = CreateServiceProvider();

        return new SuggestionsBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            services.Bank.Clock,
            new Logger<SuggestionsBackgroundService>(new LoggerFactory(new ILoggerProvider[] { loggerProvider })));
    }

    private AlertsBackgroundService CreateAlertsTask()
    {
        var serviceProvider = CreateServiceProvider();

        return new AlertsBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            services.Bank.Clock,
            new Logger<AlertsBackgroundService>(new LoggerFactory(new ILoggerProvider[] { loggerProvider })));
    }

    // Les messages du journal technique de ce niveau.
    private List<string> ReadMessages(LogLevel level)
    {
        return loggerProvider.Messages.Where(message => message.StartsWith(level + " : ")).ToList();
    }

    // Garde un compte confirmé, avec 50 € hors de tout sac et une opération d'octobre : son mois propose une suggestion.
    private User AddUserWithUnallocatedMoney()
    {
        var user = new User { Email = Guid.NewGuid().ToString("N") + "@exemple.test", PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
        services.Bags.SavedUsers.Add(user);
        var categories = DefaultCategories.CreateForUser(user.Id);
        services.Bank.Categories.SavedCategories.AddRange(categories);
        var account = services.AddBankAccount(5_000, user.Id);
        services.Bank.Transactions.SavedTransactions.Add(new BankTransaction
        {
            ExternalTransactionId = "tache-" + Guid.NewGuid(),
            TransactionDate = October.AddDays(4),
            Label = "OPERATION DE TEST",
            AmountInCents = -100,
            BankAccountId = account.Id,
            CategoryId = categories.First().Id
        });

        return user;
    }

    // Plus d'utilisateurs qu'un paquet : la tâche les parcourt tous, et chacun reçoit une seule alerte.
    [Fact]
    public async Task SuggestionsPassCalculatesEveryWaitingUserAcrossBatches()
    {
        var users = Enumerable.Range(0, SuggestionCalculationService.UserBatchSize + 5).Select(_ => AddUserWithUnallocatedMoney()).ToList();

        await CreateSuggestionsTask().RunPassAsync();

        Assert.All(users, user => Assert.Equal(October, user.LastSuggestionMonth));
        Assert.Equal(users.Count, services.Alerts.SavedAlerts.Count(alert => alert.Kind == AlertKind.NewSuggestion));
        Assert.Contains(ReadMessages(LogLevel.Information), message => message.Contains(users.Count + " calcul(s) de suggestions"));
        Assert.Empty(ReadMessages(LogLevel.Error));
    }

    // Un second passage n'a plus rien à calculer.
    [Fact]
    public async Task SecondSuggestionsPassCalculatesNothing()
    {
        AddUserWithUnallocatedMoney();
        await CreateSuggestionsTask().RunPassAsync();

        await CreateSuggestionsTask().RunPassAsync();

        Assert.Single(services.Alerts.SavedAlerts);
        Assert.Contains(ReadMessages(LogLevel.Information), message => message.Contains("0 calcul(s) de suggestions"));
    }

    // Le calcul d'un utilisateur qui échoue va au journal technique, avec son identifiant et sans son adresse,
    // et les autres sont calculés quand même.
    [Fact]
    public async Task FailingUserIsWrittenAndTheOthersAreCalculated()
    {
        var failingUser = AddUserWithUnallocatedMoney();
        var otherUsers = new[] { AddUserWithUnallocatedMoney(), AddUserWithUnallocatedMoney() };
        services.Suggestions.FailingUserId = failingUser.Id;

        await CreateSuggestionsTask().RunPassAsync();

        var error = Assert.Single(ReadMessages(LogLevel.Error));
        Assert.Contains(failingUser.Id.ToString(), error);
        Assert.DoesNotContain(failingUser.Email, error);
        Assert.Null(failingUser.LastSuggestionMonth);
        Assert.All(otherUsers, user => Assert.Equal(October, user.LastSuggestionMonth));
    }

    // Si la liste des utilisateurs ne peut pas être lue, le passage s'arrête et le note : le suivant réessaiera.
    [Fact]
    public async Task UnreadableUserListIsWrittenAndTheNextPassRetries()
    {
        var user = AddUserWithUnallocatedMoney();
        services.Suggestions.FailsWhenListingUsers = true;

        await CreateSuggestionsTask().RunPassAsync();
        Assert.Contains("La liste des suggestions à calculer ne peut pas être lue.", Assert.Single(ReadMessages(LogLevel.Error)));
        Assert.Null(user.LastSuggestionMonth);

        services.Suggestions.FailsWhenListingUsers = false;
        await CreateSuggestionsTask().RunPassAsync();

        Assert.Equal(October, user.LastSuggestionMonth);
    }

    // Garde une connexion de l'utilisateur des tests dont l'autorisation finit dans ce délai.
    private BankConnection SaveConnection(TimeSpan timeBeforeEnd)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = "session-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = services.Now + timeBeforeEnd,
            UserId = services.UserId
        };
        services.Bank.Repository.SavedConnections.Add(connection);

        return connection;
    }

    // Un passage prévient les autorisations qui finissent dans 7 jours, et efface les alertes des mois trop anciens.
    [Fact]
    public async Task AlertsPassWarnsDueConnectionsAndErasesOldAlerts()
    {
        var dueConnection = SaveConnection(TimeSpan.FromDays(3));
        SaveConnection(TimeSpan.FromDays(30));
        var oldAlert = Alert.ForSuggestions(services.UserId, new DateOnly(2026, 8, 1), services.Now);
        services.Alerts.SavedAlerts.Add(oldAlert);

        await CreateAlertsTask().RunPassAsync();

        var alert = Assert.Single(services.Alerts.SavedAlerts);
        Assert.Equal(dueConnection.Id, alert.ConnectionId);
        Assert.Single(services.EmailRequests.SavedRequests);
        Assert.Contains(ReadMessages(LogLevel.Information), message => message.Contains("1 alerte(s) d'autorisation bancaire"));
        Assert.Contains(ReadMessages(LogLevel.Information), message => message.Contains("1 alerte(s) de budget ou de suggestion effacée(s)"));
        Assert.Empty(ReadMessages(LogLevel.Error));
    }

    // L'alerte d'une connexion qui échoue va au journal technique, avec son identifiant, et les autres partent quand même.
    [Fact]
    public async Task FailingConnectionIsWrittenAndTheOthersAreWarned()
    {
        var failingConnection = SaveConnection(TimeSpan.FromDays(3));
        var otherConnection = SaveConnection(TimeSpan.FromDays(4));
        services.Bank.Repository.FailingConnectionId = failingConnection.Id;

        await CreateAlertsTask().RunPassAsync();

        Assert.Contains(failingConnection.Id.ToString(), Assert.Single(ReadMessages(LogLevel.Error)));
        Assert.Equal(otherConnection.Id, Assert.Single(services.Alerts.SavedAlerts).ConnectionId);
    }

    // Si la liste des connexions ne peut pas être lue, la panne va au journal technique, et l'effacement se fait quand même.
    [Fact]
    public async Task UnreadableConnectionListDoesNotStopTheCleanup()
    {
        services.Alerts.Connections = null;
        services.Alerts.SavedAlerts.Add(Alert.ForSuggestions(services.UserId, new DateOnly(2026, 8, 1), services.Now));

        await CreateAlertsTask().RunPassAsync();

        Assert.Contains("La liste des autorisations bancaires à renouveler ne peut pas être lue.", Assert.Single(ReadMessages(LogLevel.Error)));
        Assert.Empty(services.Alerts.SavedAlerts);
    }

    // Si l'effacement échoue, la panne va au journal technique : l'alerte des connexions est déjà partie.
    [Fact]
    public async Task FailedCleanupIsWrittenAfterTheWarnings()
    {
        SaveConnection(TimeSpan.FromDays(3));
        var alertsTask = CreateAlertsTask();
        await alertsTask.RunPassAsync();
        services.Alerts.IsDown = true;

        await alertsTask.RunPassAsync();

        Assert.Contains(ReadMessages(LogLevel.Error), message => message.Contains("L'effacement des vieilles alertes a échoué."));
        services.Alerts.IsDown = false;
        Assert.Single(services.Alerts.SavedAlerts);
    }
}
