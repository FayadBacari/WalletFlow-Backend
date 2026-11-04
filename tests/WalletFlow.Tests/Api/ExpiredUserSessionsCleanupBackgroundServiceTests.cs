using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.UserSessions;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche quotidienne qui efface les sessions expirées (MPD §3).
public class ExpiredUserSessionsCleanupBackgroundServiceTests
{
    // Crée la tâche à tester, branchée sur ce faux accès à la base.
    private static ExpiredUserSessionsCleanupBackgroundService CreateCleanupTaskWithThisRepository(
        FakeUserSessionRepository repositoryThatKeepsSessionsInMemory,
        ILogger<ExpiredUserSessionsCleanupBackgroundService>? loggerOfTheCleanupsForThisTest = null)
    {
        var clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();
        var servicesUsedByTheCleanupTask = new ServiceCollection();
        servicesUsedByTheCleanupTask.AddSingleton<IUserSessionRepository>(repositoryThatKeepsSessionsInMemory);
        servicesUsedByTheCleanupTask.AddSingleton<TimeProvider>(clockThatOnlyMovesWhenTestsAskIt);
        servicesUsedByTheCleanupTask.AddScoped<ExpiredUserSessionsCleanupService>();
        var providerOfTheCleanupTaskServices = servicesUsedByTheCleanupTask.BuildServiceProvider();

        return new ExpiredUserSessionsCleanupBackgroundService(
            providerOfTheCleanupTaskServices.GetRequiredService<IServiceScopeFactory>(),
            clockThatOnlyMovesWhenTestsAskIt,
            loggerOfTheCleanupsForThisTest ?? NullLogger<ExpiredUserSessionsCleanupBackgroundService>.Instance);
    }

    [Fact]
    public async Task OneCleanupAsksTheDatabaseToDeleteTheExpiredSessions()
    {
        var repositoryThatKeepsSessionsInMemory = new FakeUserSessionRepository(new FakeSecurityEventRepository());
        var cleanupTaskBeingTestedWithFakeRepository = CreateCleanupTaskWithThisRepository(repositoryThatKeepsSessionsInMemory);

        await cleanupTaskBeingTestedWithFakeRepository.RunOneCleanupOfTheExpiredUserSessionsAsync();

        Assert.Single(repositoryThatKeepsSessionsInMemory.DatesOfTheCleanupsRequested);
    }

    // La panne est notée dans le journal technique : sans cela, personne ne saurait que l'effacement échoue.
    [Fact]
    public async Task DatabaseFailureIsWrittenInTheTechnicalLogAndDoesNotStopTheTask()
    {
        var repositoryThatKeepsSessionsInMemory = new FakeUserSessionRepository(new FakeSecurityEventRepository()) { DatabaseIsDownDuringTheCleanup = true };
        var providerThatKeepsTheLogMessages = new LoggerProviderThatKeepsTheMessages();
        using var factoryOfTheLoggersForThisTest = new LoggerFactory(new ILoggerProvider[] { providerThatKeepsTheLogMessages });
        var cleanupTaskBeingTestedWithFakeRepository = CreateCleanupTaskWithThisRepository(
            repositoryThatKeepsSessionsInMemory,
            new Logger<ExpiredUserSessionsCleanupBackgroundService>(factoryOfTheLoggersForThisTest));

        await cleanupTaskBeingTestedWithFakeRepository.RunOneCleanupOfTheExpiredUserSessionsAsync();

        Assert.Contains(providerThatKeepsTheLogMessages.MessagesWrittenInTheLog, messageWrittenInTheTechnicalLog =>
            messageWrittenInTheTechnicalLog.StartsWith("Error") && messageWrittenInTheTechnicalLog.Contains("a échoué"));

        repositoryThatKeepsSessionsInMemory.DatabaseIsDownDuringTheCleanup = false;
        await cleanupTaskBeingTestedWithFakeRepository.RunOneCleanupOfTheExpiredUserSessionsAsync();

        Assert.Single(repositoryThatKeepsSessionsInMemory.DatesOfTheCleanupsRequested);
    }

    // La tâche tourne une fois par jour : le test ne peut pas attendre un jour pour le vérifier.
    [Fact]
    public void CleanupTaskRunsOncePerDay()
    {
        Assert.Equal(TimeSpan.FromDays(1), ExpiredUserSessionsCleanupBackgroundService.DurationBetweenTwoCleanupsOfTheSessions);
    }

    // La tâche est bien lancée avec l'API, dès son démarrage.
    [Fact]
    public async Task CleanupTaskRunsWhenTheApiStarts()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        apiStartedInMemoryForThisTest.CreateClient();

        for (var numberOfTheCheckBeingDone = 1; numberOfTheCheckBeingDone <= 100; numberOfTheCheckBeingDone++)
        {
            if (apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.DatesOfTheCleanupsRequested.Count > 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.NotEmpty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.DatesOfTheCleanupsRequested);
    }
}
