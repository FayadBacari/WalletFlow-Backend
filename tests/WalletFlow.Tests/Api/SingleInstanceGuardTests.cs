using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Infrastructure.Persistence;
using static WalletFlow.Tests.Infrastructure.AdvisoryLockTestTools;

namespace WalletFlow.Tests.Api;

// Vérifie la garde de l'instance unique sur la vraie base : une deuxième instance refuse de démarrer avant tout le reste, une
// connexion perdue ne coûte qu'un contrôle, et une instance doublée par une autre s'arrête.
public class SingleInstanceGuardTests
{
    // La vie de l'API, réduite à ce que la garde en utilise : la demande d'arrêt, comptée.
    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        private int stopCount = 0;

        public int StopCount => Volatile.Read(ref stopCount);

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        // Compte la demande d'arrêt.
        public void StopApplication()
        {
            Interlocked.Increment(ref stopCount);
        }
    }

    // Un service branché après la garde, comme une tâche de fond : il note son démarrage.
    private sealed class StartRecorder(Action onStart) : IHostedService
    {
        // Note le démarrage (nom imposé par le framework).
        public Task StartAsync(CancellationToken cancellationToken)
        {
            onStart();
            return Task.CompletedTask;
        }

        // Rien à arrêter (nom imposé par le framework).
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    // L'API de test, avec sa garde remise en tête des services hébergés, sur un verrou de test : la même que Program.cs branche,
    // sans gêner une API qui tournerait sur la même base.
    private sealed class ApiFactoryWithGuard(long key) : ApiFactoryWithFakes
    {
        // Remet la garde avant le démarrage de l'API (nom imposé par le framework).
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(CreateLock(key));
                services.Insert(0, ServiceDescriptor.Singleton<IHostedService, SingleInstanceGuard>());
            });
        }
    }

    // Crée une garde sur ce verrou, avec cet intervalle de contrôle.
    private static SingleInstanceGuard CreateGuard(SingleInstanceLock instanceLock, RecordingLifetime lifetime, TimeSpan checkInterval)
    {
        return new SingleInstanceGuard(instanceLock, lifetime, NullLogger<SingleInstanceGuard>.Instance, checkInterval);
    }

    // Une deuxième instance refuse de démarrer avec un message clair, et rien de ce qui est branché après la garde ne démarre.
    [Fact]
    public async Task SecondInstanceRefusesToStartBeforeAnythingElse()
    {
        var key = NewKey();
        await using var runningInstance = CreateLock(key);
        Assert.True(await runningInstance.TryAcquireAsync());
        var taskStarted = false;
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(CreateLock(key));
            services.AddHostedService<SingleInstanceGuard>();
            services.AddHostedService(_ => new StartRecorder(() => taskStarted = true));
        }).Build();

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Equal(SingleInstanceGuard.AnotherInstanceMessage, refusal.Message);
        Assert.False(taskStarted);
    }

    // L'API entière, telle que Program.cs la construit, refuse de démarrer quand une autre instance tourne sur la base.
    [Fact]
    public async Task WholeApiRefusesToStartWhenAnotherInstanceRuns()
    {
        var key = NewKey();
        await using var runningInstance = CreateLock(key);
        Assert.True(await runningInstance.TryAcquireAsync());
        using var secondInstance = new ApiFactoryWithGuard(key);

        var refusal = Assert.Throws<InvalidOperationException>(() => secondInstance.CreateClient());

        Assert.Equal(SingleInstanceGuard.AnotherInstanceMessage, refusal.Message);
    }

    // Seule, l'API démarre et répond, sa garde tenant le verrou.
    [Fact]
    public async Task WholeApiStartsAloneAndHoldsTheLock()
    {
        var key = NewKey();
        using var api = new ApiFactoryWithGuard(key);
        var client = api.CreateClient();
        await using var otherInstance = CreateLock(key);

        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.False(await otherInstance.TryAcquireAsync());
    }

    [Fact]
    public async Task GuardHoldsTheLockWhileRunningAndReleasesItAtStop()
    {
        var key = NewKey();
        await using var guardLock = CreateLock(key);
        await using var otherInstance = CreateLock(key);
        var lifetime = new RecordingLifetime();
        using var guard = CreateGuard(guardLock, lifetime, TimeSpan.FromDays(1));

        await guard.StartAsync(CancellationToken.None);
        var refusedWhileRunning = !await otherInstance.TryAcquireAsync();
        await guard.StopAsync(CancellationToken.None);

        Assert.True(refusedWhileRunning);
        Assert.True(await otherInstance.TryAcquireAsync());
        Assert.Equal(0, lifetime.StopCount);
    }

    // L'hôte de test arrête l'API pendant que son app.Run() l'arrête aussi : les deux arrêts n'en font qu'un, et le verrou est rendu.
    [Fact]
    public async Task TwoSimultaneousStopsMakeOneStop()
    {
        var key = NewKey();
        await using var guardLock = CreateLock(key);
        await using var otherInstance = CreateLock(key);
        using var guard = CreateGuard(guardLock, new RecordingLifetime(), TimeSpan.FromDays(1));
        await guard.StartAsync(CancellationToken.None);

        var firstStop = guard.StopAsync(CancellationToken.None);
        var secondStop = guard.StopAsync(CancellationToken.None);
        await Task.WhenAll(firstStop, secondStop);

        Assert.Same(firstStop, secondStop);
        Assert.True(await otherInstance.TryAcquireAsync());
    }

    // Libérée avant son arrêt, comme quand app.Run() libère l'API de test que l'hôte arrête encore, la garde s'arrête sans erreur
    // et rend le verrou.
    [Fact]
    public async Task StopAfterDisposeStillReleasesTheLock()
    {
        var key = NewKey();
        await using var guardLock = CreateLock(key);
        await using var otherInstance = CreateLock(key);
        var guard = CreateGuard(guardLock, new RecordingLifetime(), TimeSpan.FromDays(1));
        await guard.StartAsync(CancellationToken.None);

        guard.Dispose();
        await guard.StopAsync(CancellationToken.None);

        Assert.True(await otherInstance.TryAcquireAsync());
    }

    // Une connexion perdue ne coûte que le temps d'un contrôle : la garde reprend le verrou, sans arrêter l'API.
    [Fact]
    public async Task GuardTakesTheLockBackAfterLosingItsConnection()
    {
        var key = NewKey();
        await using var guardLock = CreateLock(key);
        var lifetime = new RecordingLifetime();
        using var guard = CreateGuard(guardLock, lifetime, TimeSpan.FromMilliseconds(100));
        await guard.StartAsync(CancellationToken.None);
        var lostProcessId = guardLock.LastProcessId;

        await TerminateHolderAsync(key);

        await WaitUntilAsync(async () => await guardLock.FindHolderAsync() is int holder && holder != lostProcessId);
        Assert.Equal(guardLock.LastProcessId, await guardLock.FindHolderAsync());
        Assert.Equal(0, lifetime.StopCount);
        await guard.StopAsync(CancellationToken.None);
    }

    // Remplace une valeur privée du verrou, pour simuler ce qu'aucun test ne peut provoquer de l'extérieur.
    private static void ReplacePrivateField(SingleInstanceLock instanceLock, string fieldName, object? value)
    {
        var field = typeof(SingleInstanceLock).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Le verrou n'a plus de champ " + fieldName + ".");
        field.SetValue(instanceLock, value);
    }

    // La connexion est perdue côté API, mais sa session tient encore le verrou dans la base : ce n'est pas une autre instance, la
    // garde n'arrête rien, et reprend le verrou dès que la session s'en va.
    [Fact]
    public async Task OwnLostSessionNeverStopsTheApi()
    {
        var key = NewKey();
        await using var guardLock = CreateLock(key);
        var lifetime = new RecordingLifetime();
        using var guard = CreateGuard(guardLock, lifetime, TimeSpan.FromMilliseconds(50));
        await guard.StartAsync(CancellationToken.None);
        var lostProcessId = guardLock.LastProcessId;

        ReplacePrivateField(guardLock, "connection", null);
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        var stopCountWhileTheSessionHolds = lifetime.StopCount;
        await TerminateHolderAsync(key);
        await WaitUntilAsync(async () => await guardLock.FindHolderAsync() is int holder && holder != lostProcessId);

        Assert.Equal(0, stopCountWhileTheSessionHolds);
        Assert.Equal(0, lifetime.StopCount);
        await guard.StopAsync(CancellationToken.None);
    }

    // Une base qui ne répond plus n'arrête pas l'API : la garde l'écrit au journal et réessaie au contrôle suivant.
    [Fact]
    public async Task DatabaseOutageNeverStopsTheApi()
    {
        var key = NewKey();
        await using var guardLock = CreateLock(key);
        var lifetime = new RecordingLifetime();
        using var guard = CreateGuard(guardLock, lifetime, TimeSpan.FromMilliseconds(50));
        await guard.StartAsync(CancellationToken.None);

        await TerminateHolderAsync(key);
        ReplacePrivateField(guardLock, "connectionString", "Host=127.0.0.1;Port=1;Database=walletflow;Username=walletflow_app;Pooling=false;Timeout=1");
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        Assert.Equal(0, lifetime.StopCount);
        await guard.StopAsync(CancellationToken.None);
    }

    // Si une autre instance prend le verrou pendant la perte, la garde arrête son API : deux instances ne restent pas ensemble.
    [Fact]
    public async Task GuardStopsTheApiWhenAnotherInstanceTookTheLock()
    {
        var key = NewKey();
        await using var guardLock = CreateLock(key);
        await using var otherInstance = CreateLock(key);
        var lifetime = new RecordingLifetime();
        using var guard = CreateGuard(guardLock, lifetime, TimeSpan.FromSeconds(1));
        await guard.StartAsync(CancellationToken.None);

        await TerminateHolderAsync(key);
        await WaitUntilAsync(() => otherInstance.TryAcquireAsync());

        await WaitUntilAsync(() => Task.FromResult(lifetime.StopCount == 1));
        await guard.StopAsync(CancellationToken.None);
        Assert.Equal(otherInstance.LastProcessId, await otherInstance.FindHolderAsync());
    }
}
