using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche horaire de la suppression (GR Suppression, SS §7) sur les services de la vraie API, avec une fausse base :
// les révocations en retard, puis l'effacement des comptes dont la suppression est demandée depuis 30 jours.
public class AccountDeletionBackgroundServiceTests
{
    // Crée la tâche à tester, branchée sur les services de cette API, et sur ce journal technique si le test en donne un.
    private static AccountDeletionBackgroundService CreateTask(ApiFactoryWithFakes api, ILogger<AccountDeletionBackgroundService>? logger = null)
    {
        return new AccountDeletionBackgroundService(
            api.Services.GetRequiredService<IServiceScopeFactory>(),
            api.Clock,
            new BackgroundTaskHealth(),
            logger ?? NullLogger<AccountDeletionBackgroundService>.Instance);
    }

    // Crée un journal technique qui garde ses messages dans cette liste.
    private static ILogger<AccountDeletionBackgroundService> CreateLogger(MemoryLoggerProvider loggerProvider)
    {
        return new LoggerFactory(new ILoggerProvider[] { loggerProvider }).CreateLogger<AccountDeletionBackgroundService>();
    }

    // Donne les messages d'erreur du journal technique.
    private static List<string> ReadErrors(MemoryLoggerProvider loggerProvider)
    {
        return loggerProvider.Messages.Where(message => message.StartsWith(LogLevel.Error + " : ", StringComparison.Ordinal)).ToList();
    }

    // Ajoute un compte dont la suppression est demandée depuis ce nombre de jours.
    private static async Task<User> AddDeletedAccountAsync(ApiFactoryWithFakes api, string email, int daysAgo)
    {
        var account = await AddAccountAsync(api, email);
        account.RequestDeletion(api.Clock.GetUtcNow().UtcDateTime.AddDays(-daysAgo));

        return account;
    }

    // Ajoute une connexion bancaire active à ce compte.
    private static BankConnection AddConnection(ApiFactoryWithFakes api, Guid userId, string externalId)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = externalId,
            BankName = "Banque",
            AuthorizationExpiresAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(90),
            UserId = userId
        };
        api.BankRepository.SavedConnections.Add(connection);

        return connection;
    }

    // À l'arrêt de l'API, le passage s'arrête sans rien commencer : l'annulation remonte au framework au lieu d'être notée comme
    // une panne, et le compte dû attend le passage suivant.
    [Fact]
    public async Task ShutdownStopsThePassBeforeTouchingAnyAccount()
    {
        using var api = new ApiFactoryWithFakes();
        var due = await AddAccountAsync(api, "a-effacer@exemple.test");
        due.RequestDeletion(api.Clock.GetUtcNow().UtcDateTime.AddDays(-30));
        AddConnection(api, due.Id, "session-a-effacer");

        var loggerProvider = new MemoryLoggerProvider();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateTask(api, CreateLogger(loggerProvider)).RunPassAsync(new CancellationToken(true)));

        Assert.Contains(due, api.AccountRepository.SavedUsers);
        Assert.Empty(api.BankProvider.RevokedSessions);
        Assert.Empty(ReadErrors(loggerProvider));
    }

    // Un passage efface le compte demandé il y a 30 jours, après avoir révoqué sa banque, et envoie l'e-mail d'effacement. Il
    // révoque la banque d'un compte encore dans ses 30 jours, sans l'effacer, et laisse les autres comptes tranquilles.
    [Fact]
    public async Task PassRevokesLateBanksThenErasesDueAccounts()
    {
        using var api = new ApiFactoryWithFakes();
        var now = api.Clock.GetUtcNow().UtcDateTime;
        var due = await AddAccountAsync(api, "a-effacer@exemple.test");
        due.RequestDeletion(now.AddDays(-30));
        var dueConnection = AddConnection(api, due.Id, "session-a-effacer");
        var waiting = await AddAccountAsync(api, "en-attente@exemple.test");
        waiting.RequestDeletion(now.AddDays(-5));
        var waitingConnection = AddConnection(api, waiting.Id, "session-en-attente");
        var active = await AddAccountAsync(api, "actif@exemple.test");
        var activeConnection = AddConnection(api, active.Id, "session-active");

        await CreateTask(api).RunPassAsync();

        Assert.DoesNotContain(due, api.AccountRepository.SavedUsers);
        Assert.Contains(waiting, api.AccountRepository.SavedUsers);
        Assert.Contains(active, api.AccountRepository.SavedUsers);
        Assert.Equal(new List<string> { "session-a-effacer", "session-en-attente" }, api.BankProvider.RevokedSessions.Order().ToList());
        Assert.Equal(
            (BankConnectionStatus.Expired, BankConnectionStatus.Expired, BankConnectionStatus.Active),
            (dueConnection.Status, waitingConnection.Status, activeConnection.Status));
        await RecoveryRequestTestValues.WaitUntilAsync(() => api.EmailSender.CountSentEmailsTo("a-effacer@exemple.test") == 1);
        Assert.Equal("Ton compte WalletFlow est effacé", api.EmailSender.FindLastSentEmailTo("a-effacer@exemple.test").Subject);
    }

    // SS §7 : un prestataire qui ne répond toujours pas n'empêche pas l'effacement ; l'autorisation finira d'elle-même.
    [Fact]
    public async Task ProviderStillDownDoesNotStopTheErasure()
    {
        using var api = new ApiFactoryWithFakes();
        var due = await AddAccountAsync(api, "a-effacer@exemple.test");
        due.RequestDeletion(api.Clock.GetUtcNow().UtcDateTime.AddDays(-31));
        AddConnection(api, due.Id, "session-a-effacer");
        api.BankProvider.RevocationFails = true;

        await CreateTask(api).RunPassAsync();

        Assert.DoesNotContain(due, api.AccountRepository.SavedUsers);
        Assert.Empty(api.BankProvider.RevokedSessions);
        await RecoveryRequestTestValues.WaitUntilAsync(() => api.EmailSender.CountSentEmailsTo("a-effacer@exemple.test") == 1);
        Assert.Equal("Ton compte WalletFlow est effacé", api.EmailSender.FindLastSentEmailTo("a-effacer@exemple.test").Subject);
    }

    // Une révocation qui échoue est notée au journal technique, sans arrêter les suivantes ni les effacements du passage.
    [Fact]
    public async Task FailingRevocationIsWrittenAndDoesNotStopThePass()
    {
        using var api = new ApiFactoryWithFakes();
        var loggerProvider = new MemoryLoggerProvider();
        var waiting = await AddDeletedAccountAsync(api, "en-attente@exemple.test", 5);
        var failingConnection = AddConnection(api, waiting.Id, "session-en-panne");
        var due = await AddDeletedAccountAsync(api, "a-effacer@exemple.test", 30);
        AddConnection(api, due.Id, "session-a-effacer");
        api.BankRepository.FailingConnectionId = failingConnection.Id;

        await CreateTask(api, CreateLogger(loggerProvider)).RunPassAsync();

        Assert.Equal(new List<string> { "session-a-effacer" }, api.BankProvider.RevokedSessions);
        Assert.DoesNotContain(due, api.AccountRepository.SavedUsers);
        Assert.Contains(waiting, api.AccountRepository.SavedUsers);
        var error = Assert.Single(ReadErrors(loggerProvider));
        Assert.Contains("La révocation de la connexion bancaire " + failingConnection.Id + " a échoué", error);
    }

    // Un effacement qui échoue est noté au journal technique, sans arrêter les suivants. Le compte reste dû : le passage suivant
    // l'efface.
    [Fact]
    public async Task FailingErasureIsWrittenAndRetriedAtTheNextPass()
    {
        using var api = new ApiFactoryWithFakes();
        var loggerProvider = new MemoryLoggerProvider();
        var failing = await AddDeletedAccountAsync(api, "en-panne@exemple.test", 40);
        var due = await AddDeletedAccountAsync(api, "a-effacer@exemple.test", 30);
        api.AccountRepository.BeforeFindById = accountId =>
        {
            if (accountId == failing.Id)
            {
                throw new InvalidOperationException("Panne simulée de ce compte.");
            }
        };
        var task = CreateTask(api, CreateLogger(loggerProvider));

        await task.RunPassAsync();

        Assert.Contains(failing, api.AccountRepository.SavedUsers);
        Assert.DoesNotContain(due, api.AccountRepository.SavedUsers);
        var error = Assert.Single(ReadErrors(loggerProvider));
        Assert.Contains("L'effacement du compte " + failing.Id + " a échoué", error);

        api.AccountRepository.BeforeFindById = null;
        await task.RunPassAsync();

        Assert.DoesNotContain(failing, api.AccountRepository.SavedUsers);
    }

    // Quand l'API s'arrête pendant une étape du passage, l'annulation remonte au framework, qui l'attend, au lieu d'être notée
    // comme une panne, et le passage ne continue pas sa liste : les comptes attendent le prochain démarrage.
    [Theory]
    [InlineData("pendant une révocation")]
    [InlineData("pendant la liste des comptes à effacer")]
    [InlineData("pendant un effacement")]
    public async Task ShutdownDuringAStepIsNotWrittenAsAFailure(string moment)
    {
        using var api = new ApiFactoryWithFakes();
        var loggerProvider = new MemoryLoggerProvider();
        var first = await AddDeletedAccountAsync(api, "premier@exemple.test", 40);
        var next = await AddDeletedAccountAsync(api, "suivant@exemple.test", 30);
        using var shutdown = new CancellationTokenSource();
        Action stopTheApi = () =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        };

        if (moment == "pendant une révocation")
        {
            AddConnection(api, first.Id, "session-premier");
            api.AccountRepository.BeforeFindById = accountId => stopTheApi();
        }
        else if (moment == "pendant la liste des comptes à effacer")
        {
            api.AccountRepository.BeforeListingErasures = stopTheApi;
        }
        else
        {
            api.AccountRepository.BeforeFindById = accountId => stopTheApi();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateTask(api, CreateLogger(loggerProvider)).RunPassAsync(shutdown.Token));

        Assert.Empty(ReadErrors(loggerProvider));
        Assert.Contains(first, api.AccountRepository.SavedUsers);
        Assert.Contains(next, api.AccountRepository.SavedUsers);
    }

    // Si la liste des connexions à révoquer ne peut pas être lue, la panne est notée et les effacements dus ont quand même lieu.
    [Fact]
    public async Task UnreadableRevocationListDoesNotStopTheErasures()
    {
        using var api = new ApiFactoryWithFakes();
        var loggerProvider = new MemoryLoggerProvider();
        var due = await AddDeletedAccountAsync(api, "a-effacer@exemple.test", 30);
        api.BankRepository.FailsWhenListingRevocations = true;

        await CreateTask(api, CreateLogger(loggerProvider)).RunPassAsync();

        Assert.DoesNotContain(due, api.AccountRepository.SavedUsers);
        Assert.Contains("La liste des connexions bancaires à révoquer ne peut pas être lue", Assert.Single(ReadErrors(loggerProvider)));
    }

    // Si la liste des comptes à effacer ne peut pas être lue, la panne est notée, les révocations du passage sont faites, et
    // aucun compte n'est effacé.
    [Fact]
    public async Task UnreadableErasureListIsWrittenAfterTheRevocations()
    {
        using var api = new ApiFactoryWithFakes();
        var loggerProvider = new MemoryLoggerProvider();
        var due = await AddDeletedAccountAsync(api, "a-effacer@exemple.test", 30);
        AddConnection(api, due.Id, "session-a-effacer");
        api.AccountRepository.BeforeListingErasures = () => throw new InvalidOperationException("Panne simulée de la liste.");

        await CreateTask(api, CreateLogger(loggerProvider)).RunPassAsync();

        Assert.Equal(new List<string> { "session-a-effacer" }, api.BankProvider.RevokedSessions);
        Assert.Contains(due, api.AccountRepository.SavedUsers);
        Assert.Contains("La liste des comptes à effacer ne peut pas être lue", Assert.Single(ReadErrors(loggerProvider)));
    }

    // La tâche fait son premier passage dès son lancement, puis un passage par heure, et s'arrête avec l'API sans noter de panne.
    [Fact]
    public async Task TaskRunsAPassAsSoonAsItStartsAndStopsCleanly()
    {
        using var api = new ApiFactoryWithFakes();
        var loggerProvider = new MemoryLoggerProvider();
        var due = await AddDeletedAccountAsync(api, "a-effacer@exemple.test", 30);
        var task = CreateTask(api, CreateLogger(loggerProvider));

        await task.StartAsync(CancellationToken.None);
        try
        {
            await RecoveryRequestTestValues.WaitUntilAsync(() => !api.AccountRepository.SavedUsers.Contains(due));
        }
        finally
        {
            await task.StopAsync(CancellationToken.None);
        }

        Assert.Equal(TimeSpan.FromHours(1), AccountDeletionBackgroundService.PassInterval);
        Assert.Empty(ReadErrors(loggerProvider));
    }
}
