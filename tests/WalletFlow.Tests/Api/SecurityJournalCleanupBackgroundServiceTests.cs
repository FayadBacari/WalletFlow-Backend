using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche quotidienne qui efface les événements du journal de sécurité de plus d'un an (SS §11).
public class SecurityJournalCleanupBackgroundServiceTests
{
    // Crée la tâche à tester, branchée sur ce faux journal.
    private static SecurityJournalCleanupBackgroundService CreateCleanupTask(
        FakeSecurityEventRepository journal,
        ILogger<SecurityJournalCleanupBackgroundService>? logger = null)
    {
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<ISecurityEventRepository>(journal);
        services.AddScoped<SecurityJournalCleanupService>();
        var serviceProvider = services.BuildServiceProvider();

        return new SecurityJournalCleanupBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            logger ?? NullLogger<SecurityJournalCleanupBackgroundService>.Instance);
    }

    [Fact]
    public async Task OneCleanupAsksTheDatabaseToDeleteTheOldEvents()
    {
        var journal = new FakeSecurityEventRepository();
        var cleanupTask = CreateCleanupTask(journal);

        await cleanupTask.RunCleanupAsync();

        Assert.Equal(1, journal.CleanupCount);
    }

    // La panne est notée dans le journal technique : sans cela, personne ne saurait que l'effacement échoue.
    [Fact]
    public async Task DatabaseFailureIsWrittenInTheTechnicalLogAndDoesNotStopTheTask()
    {
        var journal = new FakeSecurityEventRepository { IsDown = true };
        var loggerProvider = new MemoryLoggerProvider();
        using var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });
        var cleanupTask = CreateCleanupTask(journal, new Logger<SecurityJournalCleanupBackgroundService>(loggerFactory));

        await cleanupTask.RunCleanupAsync();

        Assert.Contains(loggerProvider.Messages, message =>
            message.StartsWith("Error") && message.Contains("a échoué"));

        journal.IsDown = false;
        await cleanupTask.RunCleanupAsync();

        Assert.Equal(1, journal.CleanupCount);
    }

    // La tâche tourne une fois par jour : le test ne peut pas attendre un jour pour le vérifier.
    [Fact]
    public void CleanupTaskRunsOncePerDay()
    {
        Assert.Equal(TimeSpan.FromDays(1), SecurityJournalCleanupBackgroundService.CleanupInterval);
    }

    // La tâche est bien lancée avec l'API, dès son démarrage.
    [Fact]
    public async Task CleanupTaskRunsWhenTheApiStarts()
    {
        using var api = new ApiFactoryWithFakes();
        api.CreateClient();

        for (var checkNumber = 1; checkNumber <= 100; checkNumber++)
        {
            if (api.SecurityEventRepository.CleanupCount > 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.True(api.SecurityEventRepository.CleanupCount > 0);
    }
}
