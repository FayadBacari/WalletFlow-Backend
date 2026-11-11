using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche quotidienne qui efface les comptes non confirmés (GR Auth, MPD §3).
public class UnconfirmedAccountsCleanupBackgroundServiceTests
{
    // Crée la tâche à tester, branchée sur ce faux accès à la base.
    private static UnconfirmedAccountsCleanupBackgroundService CreateCleanupTask(
        FakeUserAccountRepository accountRepository,
        ILogger<UnconfirmedAccountsCleanupBackgroundService>? logger = null)
    {
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IUserAccountRepository>(accountRepository);
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<UnconfirmedAccountsCleanupService>();
        var serviceProvider = services.BuildServiceProvider();

        return new UnconfirmedAccountsCleanupBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            logger ?? NullLogger<UnconfirmedAccountsCleanupBackgroundService>.Instance);
    }

    // La panne est notée dans le journal technique : sans cela, personne ne saurait que l'effacement échoue.
    [Fact]
    public async Task DatabaseFailureIsWrittenInTheTechnicalLog()
    {
        var accountRepository = new FakeUserAccountRepository { FailsDuringCleanup = true };
        var loggerProvider = new MemoryLoggerProvider();
        using var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });
        var cleanupTask = CreateCleanupTask(
            accountRepository,
            new Logger<UnconfirmedAccountsCleanupBackgroundService>(loggerFactory));

        await cleanupTask.RunCleanupAsync();

        Assert.Contains(loggerProvider.Messages, message =>
            message.StartsWith("Error") && message.Contains("a échoué"));
    }

    [Fact]
    public async Task OneCleanupAsksTheDatabaseToDeleteTheExpiredAccounts()
    {
        var accountRepository = new FakeUserAccountRepository();
        var cleanupTask = CreateCleanupTask(accountRepository);

        await cleanupTask.RunCleanupAsync();

        Assert.Single(accountRepository.CleanupDates);
    }

    // Une panne de la base ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    [Fact]
    public async Task DatabaseFailureDoesNotStopTheCleanupTask()
    {
        var accountRepository = new FakeUserAccountRepository { FailsDuringCleanup = true };
        var cleanupTask = CreateCleanupTask(accountRepository);

        await cleanupTask.RunCleanupAsync();

        accountRepository.FailsDuringCleanup = false;
        await cleanupTask.RunCleanupAsync();

        Assert.Single(accountRepository.CleanupDates);
    }

    // La tâche tourne une fois par jour : le test ne peut pas attendre un jour pour le vérifier.
    [Fact]
    public void CleanupTaskRunsOncePerDay()
    {
        Assert.Equal(TimeSpan.FromDays(1), UnconfirmedAccountsCleanupBackgroundService.CleanupInterval);
    }

    // La tâche est bien lancée avec l'API, dès son démarrage.
    [Fact]
    public async Task CleanupTaskRunsWhenTheApiStarts()
    {
        using var api = new ApiFactoryWithFakes();
        api.CreateClient();

        for (var checkNumber = 1; checkNumber <= 100; checkNumber++)
        {
            if (api.AccountRepository.CleanupDates.Count > 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.NotEmpty(api.AccountRepository.CleanupDates);
    }
}
