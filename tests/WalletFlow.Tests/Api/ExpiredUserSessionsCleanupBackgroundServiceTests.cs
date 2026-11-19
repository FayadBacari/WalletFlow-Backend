using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.UserSessions;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche quotidienne qui efface les sessions expirées et les appareils oubliés (MPD §3, SS §3).
public class ExpiredUserSessionsCleanupBackgroundServiceTests
{
    // Crée la tâche à tester, branchée sur ce faux accès à la base.
    private static ExpiredUserSessionsCleanupBackgroundService CreateCleanupTask(
        FakeUserSessionRepository sessionRepository,
        ILogger<ExpiredUserSessionsCleanupBackgroundService>? logger = null)
    {
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IUserSessionRepository>(sessionRepository);
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<ExpiredUserSessionsCleanupService>();
        var serviceProvider = services.BuildServiceProvider();

        return new ExpiredUserSessionsCleanupBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            new BackgroundTaskHealth(),
            logger ?? NullLogger<ExpiredUserSessionsCleanupBackgroundService>.Instance);
    }

    [Fact]
    public async Task OneCleanupAsksTheDatabaseToDeleteTheExpiredSessionsAndTheForgottenDevices()
    {
        var sessionRepository = new FakeUserSessionRepository();
        var cleanupTask = CreateCleanupTask(sessionRepository);

        await cleanupTask.RunPassAsync();

        Assert.Single(sessionRepository.CleanupDates);
        Assert.Single(sessionRepository.DeviceCleanupDates);
    }

    // La panne est notée dans le journal technique : sans cela, personne ne saurait que l'effacement échoue.
    [Fact]
    public async Task DatabaseFailureIsWrittenInTheTechnicalLogAndDoesNotStopTheTask()
    {
        var sessionRepository = new FakeUserSessionRepository() { FailsDuringCleanup = true };
        var loggerProvider = new MemoryLoggerProvider();
        using var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });
        var cleanupTask = CreateCleanupTask(
            sessionRepository,
            new Logger<ExpiredUserSessionsCleanupBackgroundService>(loggerFactory));

        await cleanupTask.RunPassAsync();

        Assert.Contains(loggerProvider.Messages, message =>
            message.StartsWith("Error", StringComparison.Ordinal) && message.Contains("a échoué"));

        sessionRepository.FailsDuringCleanup = false;
        await cleanupTask.RunPassAsync();

        Assert.Single(sessionRepository.CleanupDates);
    }

    // L'arrêt de l'API pendant l'effacement n'est pas une panne : rien n'est noté comme une erreur.
    [Fact]
    public async Task ShutdownDuringTheCleanupIsNotWrittenAsAFailure()
    {
        var loggerProvider = new MemoryLoggerProvider();
        using var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });
        var cleanupTask = CreateCleanupTask(
            new FakeUserSessionRepository(),
            new Logger<ExpiredUserSessionsCleanupBackgroundService>(loggerFactory));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cleanupTask.RunPassAsync(new CancellationToken(true)));

        Assert.DoesNotContain(loggerProvider.Messages, message => message.StartsWith("Error", StringComparison.Ordinal));
    }

    // La tâche tourne une fois par jour : le test ne peut pas attendre un jour pour le vérifier.
    [Fact]
    public void CleanupTaskRunsOncePerDay()
    {
        Assert.Equal(TimeSpan.FromDays(1), ExpiredUserSessionsCleanupBackgroundService.CleanupInterval);
    }

    // La tâche est bien lancée avec l'API, dès son démarrage.
    [Fact]
    public async Task CleanupTaskRunsWhenTheApiStarts()
    {
        using var api = new ApiFactoryWithFakes();
        api.CreateClient();

        for (var checkNumber = 1; checkNumber <= 100; checkNumber++)
        {
            if (api.SessionRepository.CleanupDates.Count > 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.NotEmpty(api.SessionRepository.CleanupDates);
    }
}
