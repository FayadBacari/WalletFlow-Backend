using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.Support;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche quotidienne qui efface les demandes de support envoyées il y a plus d'un an (GR Admin, SS §7).
public class SupportRequestsCleanupBackgroundServiceTests
{
    // Crée la tâche à tester, branchée sur cette fausse base et cette fausse horloge.
    private static SupportRequestsCleanupBackgroundService CreateCleanupTask(
        FakeSupportRequestRepository repository,
        FakeTimeProvider clock,
        ILogger<SupportRequestsCleanupBackgroundService>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISupportRequestRepository>(repository);
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<SupportRequestCleanupService>();
        var serviceProvider = services.BuildServiceProvider();

        return new SupportRequestsCleanupBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            logger ?? NullLogger<SupportRequestsCleanupBackgroundService>.Instance);
    }

    // Un passage efface les demandes de plus d'un an et garde les autres.
    [Fact]
    public async Task OneCleanupErasesOnlyTheRequestsOlderThanOneYear()
    {
        var clock = new FakeTimeProvider();
        var now = clock.GetUtcNow().UtcDateTime;
        var repository = new FakeSupportRequestRepository();
        var tooOld = SupportRequest.FromVisitor("visiteur@exemple.test", "Ancienne", now.AddYears(-1).AddMinutes(-1));
        var recent = SupportRequest.FromVisitor("visiteur@exemple.test", "Récente", now.AddMonths(-11));
        repository.SavedRequests.AddRange(new[] { tooOld, recent });

        await CreateCleanupTask(repository, clock).RunCleanupAsync();

        Assert.Equal(recent, Assert.Single(repository.SavedRequests));
    }

    // La panne est notée dans le journal technique, sans arrêter la tâche : le passage suivant efface ce qui restait.
    [Fact]
    public async Task DatabaseFailureIsWrittenInTheTechnicalLogAndDoesNotStopTheTask()
    {
        var clock = new FakeTimeProvider();
        var repository = new FakeSupportRequestRepository { IsDown = true };
        repository.SavedRequests.Add(SupportRequest.FromVisitor("visiteur@exemple.test", "Ancienne", clock.GetUtcNow().UtcDateTime.AddYears(-2)));
        var loggerProvider = new MemoryLoggerProvider();
        using var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });
        var cleanupTask = CreateCleanupTask(repository, clock, new Logger<SupportRequestsCleanupBackgroundService>(loggerFactory));

        await cleanupTask.RunCleanupAsync();

        Assert.Contains(loggerProvider.Messages, message => message.StartsWith("Error") && message.Contains("a échoué"));

        repository.IsDown = false;
        await cleanupTask.RunCleanupAsync();

        Assert.Empty(repository.SavedRequests);
    }

    // La tâche tourne une fois par jour : le test ne peut pas attendre un jour pour le vérifier.
    [Fact]
    public void CleanupTaskRunsOncePerDay()
    {
        Assert.Equal(TimeSpan.FromDays(1), SupportRequestsCleanupBackgroundService.CleanupInterval);
    }

    // La tâche est bien lancée avec l'API, dès son démarrage.
    [Fact]
    public async Task CleanupTaskRunsWhenTheApiStarts()
    {
        using var api = new ApiFactoryWithFakes();
        api.SupportRepository.SavedRequests.Add(
            SupportRequest.FromVisitor("visiteur@exemple.test", "Ancienne", api.Clock.GetUtcNow().UtcDateTime.AddYears(-2)));
        api.CreateClient();

        for (var checkNumber = 1; checkNumber <= 100 && api.SupportRepository.SavedRequests.Count > 0; checkNumber++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Empty(api.SupportRepository.SavedRequests);
    }
}
