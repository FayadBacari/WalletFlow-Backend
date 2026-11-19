using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WalletFlow.Api.BackgroundTasks;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la base commune des tâches de fond : le premier passage au démarrage, puis un passage après chaque attente, l'arrêt
// propre, et un accès à la base par étape. Vérifie aussi que chaque tâche de l'API s'appuie sur elle, avec sa propre attente.
public class RecurringBackgroundServiceTests
{
    // Un service créé pour un seul accès à la base : il note sa fin.
    private sealed class ScopedProbe : IDisposable
    {
        public bool IsDisposed { get; private set; } = false;

        // Note la fin de l'accès (nom imposé par .NET).
        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    // Une tâche de test, sur l'horloge réelle : elle compte ses passages, et lance une étape à la demande.
    private sealed class CountingTask(IServiceScopeFactory scopeFactory, TimeSpan interval, BackgroundTaskHealth? health = null)
        : RecurringBackgroundService(scopeFactory, TimeProvider.System, interval, health ?? new BackgroundTaskHealth())
    {
        private int passCount = 0;

        public int PassCount => Volatile.Read(ref passCount);

        // Compte le passage.
        public override Task RunPassAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref passCount);
            return Task.CompletedTask;
        }

        // Lance une étape comme le ferait une vraie tâche.
        public Task<TResult> RunStepAsync<TResult>(Func<ScopedProbe, Task<TResult>> step, CancellationToken cancellationToken = default)
        {
            return RunInScopeAsync(step, cancellationToken);
        }
    }

    // Une tâche de test dont chaque passage fait une étape, qui échoue sur demande, ou signale lui-même un échec.
    private sealed class StepTask(IServiceScopeFactory scopeFactory, BackgroundTaskHealth health)
        : RecurringBackgroundService(scopeFactory, TimeProvider.System, TimeSpan.FromMilliseconds(20), health)
    {
        public volatile bool StepFails = false;
        public volatile bool ReportsFailure = false;
        public volatile bool AlternatesWithIdlePasses = false;
        private int passNumber = 0;

        // Fait une étape, et rattrape sa panne comme une vraie tâche. Un passage sur deux peut être à vide, sans aucune étape.
        public override async Task RunPassAsync(CancellationToken cancellationToken = default)
        {
            if (AlternatesWithIdlePasses && Interlocked.Increment(ref passNumber) % 2 == 0)
            {
                ReportIdlePass();
                return;
            }

            try
            {
                await RunInScopeAsync<ScopedProbe, int>(_ => StepFails ? throw new InvalidOperationException("Panne de test.") : Task.FromResult(0), cancellationToken);
            }
            catch (InvalidOperationException)
            {
            }

            if (ReportsFailure)
            {
                ReportFailure();
            }
        }
    }

    // Crée les services d'un accès à la base, réduits à la sonde.
    private static ServiceProvider CreateServices()
    {
        return new ServiceCollection().AddScoped<ScopedProbe>().BuildServiceProvider();
    }

    [Fact]
    public async Task TaskRunsAPassAtStartThenAfterEachWait()
    {
        using var services = CreateServices();
        var task = new CountingTask(services.GetRequiredService<IServiceScopeFactory>(), TimeSpan.FromMilliseconds(20));

        await task.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => task.PassCount >= 3);
        }
        finally
        {
            await task.StopAsync(CancellationToken.None);
        }

        Assert.True(task.ExecuteTask!.IsCompletedSuccessfully);
    }

    // Avec un jour d'attente, rien ne repasse avant ce jour, et l'arrêt pendant l'attente termine la tâche normalement : ce n'est
    // pas une panne.
    [Fact]
    public async Task TaskWaitsItsIntervalAndStopsNormallyDuringTheWait()
    {
        using var services = CreateServices();
        var task = new CountingTask(services.GetRequiredService<IServiceScopeFactory>(), TimeSpan.FromDays(1));

        await task.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => task.PassCount == 1);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        var passCountBeforeStop = task.PassCount;
        await task.StopAsync(CancellationToken.None);

        Assert.Equal(1, passCountBeforeStop);
        Assert.True(task.ExecuteTask!.IsCompletedSuccessfully);
    }

    // Chaque étape a son propre accès à la base, fermé dès la fin de l'étape.
    [Fact]
    public async Task EachStepHasItsOwnAccessClosedAfterIt()
    {
        using var services = CreateServices();
        var task = new CountingTask(services.GetRequiredService<IServiceScopeFactory>(), TimeSpan.FromDays(1));

        var first = await task.RunStepAsync(probe => Task.FromResult(probe));
        var second = await task.RunStepAsync(probe => Task.FromResult(probe));

        Assert.NotSame(first, second);
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
    }

    // Après l'arrêt de l'API, aucune étape ne commence : pas même l'ouverture de son accès à la base.
    [Fact]
    public async Task NoStepStartsAfterTheStop()
    {
        using var services = CreateServices();
        var task = new CountingTask(services.GetRequiredService<IServiceScopeFactory>(), TimeSpan.FromDays(1));
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        var stepStarted = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.RunStepAsync(probe =>
        {
            stepStarted = true;
            return Task.FromResult(0);
        }, stop.Token));

        Assert.False(stepStarted);
    }

    // Une étape qui échoue fait échouer son passage, même rattrapée par la tâche : au troisième, la tâche est en panne. Un passage
    // réussi efface la panne.
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public async Task FailedStepOrReportedFailureFailsThePass(bool stepFails, bool reportsFailure, bool alternatesWithIdlePasses)
    {
        using var services = CreateServices();
        var health = new BackgroundTaskHealth();
        var task = new StepTask(services.GetRequiredService<IServiceScopeFactory>(), health)
        {
            StepFails = stepFails,
            ReportsFailure = reportsFailure,
            AlternatesWithIdlePasses = alternatesWithIdlePasses
        };

        await task.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => health.FindFailingTasks(DateTimeOffset.UtcNow).Count == 1);
            task.StepFails = false;
            task.ReportsFailure = false;
            task.AlternatesWithIdlePasses = false;
            await WaitUntilAsync(() => health.FindFailingTasks(DateTimeOffset.UtcNow).Count == 0);
        }
        finally
        {
            await task.StopAsync(CancellationToken.None);
        }

        Assert.Equal(nameof(StepTask), task.Name);
    }

    // Des passages réussis ne font jamais de panne.
    [Fact]
    public async Task SuccessfulPassesAreNeverAnAlert()
    {
        using var services = CreateServices();
        var health = new BackgroundTaskHealth();
        var task = new StepTask(services.GetRequiredService<IServiceScopeFactory>(), health);

        await task.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var failingTasks = health.FindFailingTasks(DateTimeOffset.UtcNow);
        await task.StopAsync(CancellationToken.None);

        Assert.Empty(failingTasks);
    }

    // Chaque tâche de l'API passe par la base commune, avec l'attente que ses règles demandent.
    [Theory]
    [InlineData(typeof(AccountDeletionBackgroundService), 60 * 60)]
    [InlineData(typeof(AlertsBackgroundService), 60 * 60)]
    [InlineData(typeof(BankBalancesBackgroundService), 15 * 60)]
    [InlineData(typeof(EmailRequestsBackgroundService), 30)]
    [InlineData(typeof(ExpiredUserSessionsCleanupBackgroundService), 24 * 60 * 60)]
    [InlineData(typeof(SecurityJournalCleanupBackgroundService), 24 * 60 * 60)]
    [InlineData(typeof(SuggestionsBackgroundService), 60 * 60)]
    [InlineData(typeof(SupportRequestsCleanupBackgroundService), 24 * 60 * 60)]
    [InlineData(typeof(TotpSecretReEncryptionBackgroundService), 24 * 60 * 60)]
    [InlineData(typeof(UnconfirmedAccountsCleanupBackgroundService), 24 * 60 * 60)]
    public void EveryTaskWaitsItsOwnInterval(Type taskType, int expectedSeconds)
    {
        using var api = new ApiFactoryWithFakes();

        var task = (RecurringBackgroundService)ActivatorUtilities.CreateInstance(api.Services, taskType);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), task.Interval);
    }

    // Une nouvelle tâche de fond doit passer par la base commune, et prendre sa place dans la liste au-dessus.
    [Fact]
    public void EveryBackgroundTaskOfTheApiUsesTheCommonBase()
    {
        var taskTypes = typeof(RecurringBackgroundService).Assembly.GetTypes()
            .Where(type => typeof(BackgroundService).IsAssignableFrom(type) && !type.IsAbstract)
            .ToList();

        Assert.All(taskTypes, type => Assert.True(type.IsSubclassOf(typeof(RecurringBackgroundService)), type.Name));
        Assert.Equal(10, taskTypes.Count);
    }
}
