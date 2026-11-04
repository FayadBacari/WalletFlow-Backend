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
    private static UnconfirmedAccountsCleanupBackgroundService CreateCleanupTaskWithThisRepository(
        FakeUserAccountRepository repositoryThatKeepsAccountsInMemory,
        ILogger<UnconfirmedAccountsCleanupBackgroundService>? loggerOfTheCleanupsForThisTest = null)
    {
        var clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();
        var servicesUsedByTheCleanupTask = new ServiceCollection();
        servicesUsedByTheCleanupTask.AddSingleton<IUserAccountRepository>(repositoryThatKeepsAccountsInMemory);
        servicesUsedByTheCleanupTask.AddSingleton<TimeProvider>(clockThatOnlyMovesWhenTestsAskIt);
        servicesUsedByTheCleanupTask.AddScoped<UnconfirmedAccountsCleanupService>();
        var providerOfTheCleanupTaskServices = servicesUsedByTheCleanupTask.BuildServiceProvider();

        return new UnconfirmedAccountsCleanupBackgroundService(
            providerOfTheCleanupTaskServices.GetRequiredService<IServiceScopeFactory>(),
            clockThatOnlyMovesWhenTestsAskIt,
            loggerOfTheCleanupsForThisTest ?? NullLogger<UnconfirmedAccountsCleanupBackgroundService>.Instance);
    }

    // La panne est notée dans le journal technique : sans cela, personne ne saurait que l'effacement échoue.
    [Fact]
    public async Task DatabaseFailureIsWrittenInTheTechnicalLog()
    {
        var repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository { DatabaseIsDownDuringTheCleanup = true };
        var providerThatKeepsTheLogMessages = new LoggerProviderThatKeepsTheMessages();
        using var factoryOfTheLoggersForThisTest = new LoggerFactory(new ILoggerProvider[] { providerThatKeepsTheLogMessages });
        var cleanupTaskBeingTestedWithFakeRepository = CreateCleanupTaskWithThisRepository(
            repositoryThatKeepsAccountsInMemory,
            new Logger<UnconfirmedAccountsCleanupBackgroundService>(factoryOfTheLoggersForThisTest));

        await cleanupTaskBeingTestedWithFakeRepository.RunOneCleanupOfTheUnconfirmedAccountsAsync();

        Assert.Contains(providerThatKeepsTheLogMessages.MessagesWrittenInTheLog, messageWrittenInTheTechnicalLog =>
            messageWrittenInTheTechnicalLog.StartsWith("Error") && messageWrittenInTheTechnicalLog.Contains("a échoué"));
    }

    [Fact]
    public async Task OneCleanupAsksTheDatabaseToDeleteTheExpiredAccounts()
    {
        var repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository();
        var cleanupTaskBeingTestedWithFakeRepository = CreateCleanupTaskWithThisRepository(repositoryThatKeepsAccountsInMemory);

        await cleanupTaskBeingTestedWithFakeRepository.RunOneCleanupOfTheUnconfirmedAccountsAsync();

        Assert.Single(repositoryThatKeepsAccountsInMemory.DatesOfTheCleanupsRequested);
    }

    // Une panne de la base ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    [Fact]
    public async Task DatabaseFailureDoesNotStopTheCleanupTask()
    {
        var repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository { DatabaseIsDownDuringTheCleanup = true };
        var cleanupTaskBeingTestedWithFakeRepository = CreateCleanupTaskWithThisRepository(repositoryThatKeepsAccountsInMemory);

        await cleanupTaskBeingTestedWithFakeRepository.RunOneCleanupOfTheUnconfirmedAccountsAsync();

        repositoryThatKeepsAccountsInMemory.DatabaseIsDownDuringTheCleanup = false;
        await cleanupTaskBeingTestedWithFakeRepository.RunOneCleanupOfTheUnconfirmedAccountsAsync();

        Assert.Single(repositoryThatKeepsAccountsInMemory.DatesOfTheCleanupsRequested);
    }

    // La tâche tourne une fois par jour : le test ne peut pas attendre un jour pour le vérifier.
    [Fact]
    public void CleanupTaskRunsOncePerDay()
    {
        Assert.Equal(TimeSpan.FromDays(1), UnconfirmedAccountsCleanupBackgroundService.DurationBetweenTwoCleanupsOfTheAccounts);
    }

    // La tâche est bien lancée avec l'API, dès son démarrage.
    [Fact]
    public async Task CleanupTaskRunsWhenTheApiStarts()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        apiStartedInMemoryForThisTest.CreateClient();

        for (var numberOfTheCheckBeingDone = 1; numberOfTheCheckBeingDone <= 100; numberOfTheCheckBeingDone++)
        {
            if (apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.DatesOfTheCleanupsRequested.Count > 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.NotEmpty(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.DatesOfTheCleanupsRequested);
    }
}
