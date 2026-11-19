using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Api.Configuration;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche de fond qui met à jour les connexions bancaires, 4 fois par jour au plus (GR Comptes), sur les services de
// la vraie API, avec un faux prestataire et une fausse base. Les règles de la mise à jour elle-même ont leurs propres tests
// (BankBalanceUpdateServiceTests) : ici, le passage, son horaire commun, ses pannes et l'arrêt de l'API.
public sealed class BankBalancesBackgroundServiceTests : IDisposable
{
    private const string StableId = "stable-1";
    private const int FirstBalance = 152_034;
    private const int NewBalance = 99_500;

    private readonly MemoryLoggerProvider loggerProvider = new MemoryLoggerProvider();
    private readonly LoggerFactory loggerFactory;

    // Prépare le journal technique que lit chaque test.
    public BankBalancesBackgroundServiceTests()
    {
        loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });
    }

    // Ferme le journal technique à la fin du test.
    public void Dispose()
    {
        loggerFactory.Dispose();
    }

    // Crée la tâche à tester, branchée sur les services de cette API et sur le journal technique du test.
    private BankBalancesBackgroundService CreateTask(ApiFactoryWithFakes api)
    {
        return new BankBalancesBackgroundService(
            api.Services.GetRequiredService<IServiceScopeFactory>(),
            api.Clock,
            new BackgroundTaskHealth(),
            new Logger<BankBalancesBackgroundService>(loggerFactory));
    }

    // Connecte la banque du faux prestataire au compte des tests, par les vraies routes, avec un compte courant en euros.
    // Renvoie l'identifiant de la connexion.
    private static async Task<Guid> ConnectBankAsync(ApiFactoryWithFakes api)
    {
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        var start = await PostAsync(client, "/api/bank-connections", new { bankName = FakeBankDataProvider.BankName }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var code = "code-" + Guid.NewGuid().ToString("N");
        api.BankProvider.AddSession(code, new TestBankAccount(StableId, "Compte courant", "EUR", FirstBalance));
        var state = api.BankProvider.AuthorizationRequests.Last().State;
        var finish = await PostAsync(client, "/api/bank-connections/finish", new { code = code, state = state }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);

        return Assert.Single(api.BankRepository.SavedConnections).Id;
    }

    // Envoie ce corps JSON à cette route, avec le jeton anti-CSRF.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string address, object body, string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(body) };
        request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);

        return await client.SendAsync(request);
    }

    // Donne le solde enregistré du compte courant.
    private static int ReadBalance(ApiFactoryWithFakes api)
    {
        return api.BankRepository.SavedAccounts.Single(account => account.ExternalAccountId == StableId).BalanceInCents;
    }

    // Donne les messages du journal technique écrits à ce niveau.
    private List<string> ReadMessages(LogLevel level)
    {
        return loggerProvider.Messages.Where(message => message.StartsWith(level + " : ", StringComparison.Ordinal)).ToList();
    }

    // Un passage lit le nouveau solde chez la banque sans transmettre la présence de l'utilisateur, qui n'est pas là, et note au
    // journal technique combien de connexions il a mises à jour.
    [Fact]
    public async Task PassUpdatesTheBalanceWithoutTheUser()
    {
        using var api = new ApiFactoryWithFakes();
        await ConnectBankAsync(api);
        api.BankProvider.ChangeBalance(StableId, NewBalance);
        var readCount = api.BankProvider.BalanceReadPresences.Count;

        await CreateTask(api).RunPassAsync();

        Assert.Equal(NewBalance, ReadBalance(api));
        Assert.Null(Assert.Single(api.BankProvider.BalanceReadPresences.Skip(readCount)));
        Assert.Contains(ReadMessages(LogLevel.Information), message => message.Contains("1 connexion(s) bancaire(s) mise(s) à jour"));
    }

    // L'horaire des mises à jour sert à tous les passages, même si chacun crée son propre accès aux services : une connexion
    // mise à jour attend 6 heures avant la suivante, soit 4 mises à jour par jour au plus.
    [Fact]
    public async Task ConnectionWaitsSixHoursBetweenTwoPasses()
    {
        using var api = new ApiFactoryWithFakes();
        await ConnectBankAsync(api);
        var task = CreateTask(api);
        await task.RunPassAsync();
        api.BankProvider.ChangeBalance(StableId, NewBalance);

        api.Clock.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(1));
        await task.RunPassAsync();

        Assert.Equal(FirstBalance, ReadBalance(api));

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        await task.RunPassAsync();

        Assert.Equal(NewBalance, ReadBalance(api));
    }

    // Une panne de la base est notée au journal technique sans arrêter la tâche : un passage suivant met la connexion à jour.
    [Fact]
    public async Task DatabaseFailureIsWrittenInTheTechnicalLogAndDoesNotStopTheTask()
    {
        using var api = new ApiFactoryWithFakes();
        var connectionId = await ConnectBankAsync(api);
        api.BankProvider.ChangeBalance(StableId, NewBalance);
        api.BankRepository.FailingConnectionId = connectionId;
        var task = CreateTask(api);

        await task.RunPassAsync();

        Assert.Contains(ReadMessages(LogLevel.Error), message => message.Contains("a échoué"));
        Assert.Equal(FirstBalance, ReadBalance(api));

        api.BankRepository.FailingConnectionId = null;
        api.Clock.Advance(TimeSpan.FromHours(6));
        await task.RunPassAsync();

        Assert.Equal(NewBalance, ReadBalance(api));
    }

    // Un délai dépassé chez le prestataire est une panne comme une autre : son jeton à lui est annulé, pas celui de l'API. Le
    // passage la note au journal technique et se termine normalement.
    [Fact]
    public async Task ProviderTimeoutIsWrittenAsAFailure()
    {
        using var api = new ApiFactoryWithFakes();
        await ConnectBankAsync(api);
        api.BankProvider.BeforeBalanceRead = () => throw new TaskCanceledException("Délai dépassé chez le prestataire.");

        await CreateTask(api).RunPassAsync();

        Assert.Contains(ReadMessages(LogLevel.Error), message => message.Contains("a échoué"));
    }

    // Quand l'API s'arrête pendant l'appel à la banque, l'annulation remonte au framework, qui l'attend, au lieu d'être notée
    // comme une panne.
    [Fact]
    public async Task ShutdownDuringTheBankCallIsNotWrittenAsAFailure()
    {
        using var api = new ApiFactoryWithFakes();
        await ConnectBankAsync(api);
        using var shutdown = new CancellationTokenSource();
        api.BankProvider.BeforeBalanceRead = () =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateTask(api).RunPassAsync(shutdown.Token));

        Assert.Empty(ReadMessages(LogLevel.Error));
    }

    // La tâche fait son premier passage dès son lancement, puis attend 15 minutes entre deux passages : 4 fois par heure, une
    // connexion due depuis peu n'attend jamais longtemps. Elle s'arrête avec l'API sans noter de panne.
    [Fact]
    public async Task TaskRunsAPassAsSoonAsItStartsAndStopsCleanly()
    {
        using var api = new ApiFactoryWithFakes();
        await ConnectBankAsync(api);
        api.BankProvider.ChangeBalance(StableId, NewBalance);
        var task = CreateTask(api);

        await task.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => ReadBalance(api) == NewBalance);
        }
        finally
        {
            await task.StopAsync(CancellationToken.None);
        }

        Assert.Equal(TimeSpan.FromMinutes(15), BankBalancesBackgroundService.PassInterval);
        Assert.Empty(ReadMessages(LogLevel.Error));
    }
}
