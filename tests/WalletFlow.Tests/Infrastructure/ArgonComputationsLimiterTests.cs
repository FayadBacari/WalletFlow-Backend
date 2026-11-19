using WalletFlow.Application.Security;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la limite des calculs Argon2id faits en même temps : c'est elle qui protège la mémoire du serveur.
public class ArgonComputationsLimiterTests
{
    [Fact]
    public async Task NeverMoreComputationsRunTogetherThanTheLimit()
    {
        var limiter = new ArgonComputationsLimiter(2);
        var countersLock = new object();
        var runningCount = 0;
        var highestRunningCount = 0;
        var computations = new List<Task<int>>();

        for (var computationNumber = 1; computationNumber <= 8; computationNumber++)
        {
            computations.Add(Task.Run(() => limiter.RunAsync(() =>
            {
                lock (countersLock)
                {
                    runningCount = runningCount + 1;
                    highestRunningCount = Math.Max(highestRunningCount, runningCount);
                }

                Thread.Sleep(20);

                lock (countersLock)
                {
                    runningCount = runningCount - 1;
                }

                return 1;
            })));
        }

        var results = await Task.WhenAll(computations);

        Assert.Equal(8, results.Sum());
        Assert.InRange(highestRunningCount, 1, 2);
    }

    // Un calcul qui échoue rend quand même sa place : sinon, quelques erreurs suffiraient à bloquer toutes les connexions.
    [Fact]
    public async Task ComputationThatFailsStillGivesBackItsPlace()
    {
        var limiter = new ArgonComputationsLimiter(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            limiter.RunAsync<int>(() => throw new InvalidOperationException("calcul en échec")));

        var nextComputation = limiter.RunAsync(() => 42);
        var firstFinished = await Task.WhenAny(nextComputation, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(nextComputation, firstFinished);
        Assert.Equal(42, await nextComputation);
    }

    [Fact]
    public void LimiterRefusesToAllowNoComputationAtAll()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArgonComputationsLimiter(0));
    }

    // Occupe la seule place du limiteur avec un calcul qui attend le signal, et rend la main une fois le calcul commencé.
    private static async Task<(Task<int> Holder, ManualResetEventSlim Release)> HoldTheOnlySlotAsync(ArgonComputationsLimiter limiter)
    {
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var holder = Task.Run(() => limiter.RunAsync(() =>
        {
            started.Set();
            release.Wait();
            return 0;
        }));
        Assert.True(await Task.Run(() => started.Wait(TimeSpan.FromSeconds(5))));
        return (holder, release);
    }

    // Attend que ce nombre de calculs soient dans la file, 5 secondes au plus.
    private static async Task WaitForQueueAsync(ArgonComputationsLimiter limiter, int waitingCount)
    {
        for (var checkNumber = 0; checkNumber < 500 && limiter.WaitingCount != waitingCount; checkNumber++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(waitingCount, limiter.WaitingCount);
    }

    // SS §6 : une rafale de connexions ne s'empile pas sans fin. Quand la file est pleine (4 calculs par place), le calcul de
    // trop est refusé tout de suite ; ceux qui attendaient se font quand même, une fois la place rendue.
    [Fact]
    public async Task FullQueueRefusesTheNextComputationAtOnce()
    {
        var limiter = new ArgonComputationsLimiter(1);
        var (holder, release) = await HoldTheOnlySlotAsync(limiter);
        var waiting = Enumerable.Range(0, ArgonComputationsLimiter.WaitingPerSlot).Select(_ => Task.Run(() => limiter.RunAsync(() => 1))).ToList();
        await WaitForQueueAsync(limiter, ArgonComputationsLimiter.WaitingPerSlot);

        await Assert.ThrowsAsync<ServerBusyException>(() => limiter.RunAsync(() => 1));

        release.Set();
        Assert.Equal(ArgonComputationsLimiter.WaitingPerSlot, (await Task.WhenAll(waiting)).Sum());
        await holder;
        Assert.Equal(0, limiter.WaitingCount);
    }

    // Un calcul qui attend plus longtemps que l'attente maximale est refusé, et quitte la file.
    [Fact]
    public async Task WaitLongerThanTheMaximumIsRefused()
    {
        var limiter = new ArgonComputationsLimiter(1, TimeSpan.FromMilliseconds(100));
        var (holder, release) = await HoldTheOnlySlotAsync(limiter);

        await Assert.ThrowsAsync<ServerBusyException>(() => limiter.RunAsync(() => 1));

        Assert.Equal(0, limiter.WaitingCount);
        release.Set();
        await holder;
    }

    // Une requête abandonnée par son client quitte la file, et son calcul ne part jamais.
    [Fact]
    public async Task AbandonedRequestLeavesTheQueueWithoutComputing()
    {
        var limiter = new ArgonComputationsLimiter(1);
        var (holder, release) = await HoldTheOnlySlotAsync(limiter);
        using var abandon = new CancellationTokenSource();
        var computed = false;
        var waiting = limiter.RunAsync(() =>
        {
            computed = true;
            return 1;
        }, abandon.Token);
        await WaitForQueueAsync(limiter, 1);

        abandon.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(0, limiter.WaitingCount);
        release.Set();
        await holder;
        Assert.False(computed);
    }

    // Une requête déjà abandonnée ne calcule rien, même si une place est libre.
    [Fact]
    public async Task AlreadyAbandonedRequestNeverComputes()
    {
        var limiter = new ArgonComputationsLimiter(1);
        var computed = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.RunAsync(() =>
        {
            computed = true;
            return 1;
        }, new CancellationToken(true)));

        Assert.False(computed);
        Assert.Equal(42, await limiter.RunAsync(() => 42));
    }
}
