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
}
