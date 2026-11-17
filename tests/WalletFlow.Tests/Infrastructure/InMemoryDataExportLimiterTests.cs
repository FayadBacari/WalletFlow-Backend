using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la limite des téléchargements de données (SS §6) : 3 par heure et par utilisateur, l'heure partant du premier essai.
public class InMemoryDataExportLimiterTests
{
    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly InMemoryDataExportLimiter limiter;

    public InMemoryDataExportLimiterTests()
    {
        limiter = new InMemoryDataExportLimiter(clock);
    }

    // Les 3 premiers essais de l'heure passent, le 4e non, jusqu'à la fin de l'heure du premier : alors tout repart.
    [Fact]
    public void ThreeTriesPassThenTheHourMustEnd()
    {
        var userId = Guid.NewGuid();

        Assert.True(limiter.TryCountExport(userId));
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(limiter.TryCountExport(userId));
        Assert.True(limiter.TryCountExport(userId));
        Assert.False(limiter.TryCountExport(userId));
        clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromTicks(1));
        Assert.False(limiter.TryCountExport(userId));
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(limiter.TryCountExport(userId));
        Assert.True(limiter.TryCountExport(userId));
        Assert.True(limiter.TryCountExport(userId));
        Assert.False(limiter.TryCountExport(userId));
    }

    // Chaque utilisateur a son compteur : les essais d'un autre ne comptent pas.
    [Fact]
    public void EachUserHasItsOwnCount()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        for (var tryNumber = 1; tryNumber <= 3; tryNumber++)
        {
            Assert.True(limiter.TryCountExport(otherUserId));
        }

        Assert.False(limiter.TryCountExport(otherUserId));
        Assert.True(limiter.TryCountExport(userId));
    }

    // Des essais lancés en même temps ne dépassent jamais 3 : le compteur est protégé.
    [Fact]
    public async Task SimultaneousTriesNeverPassMoreThanThree()
    {
        var userId = Guid.NewGuid();
        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tries = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(async () =>
            {
                await startSignal.Task;
                return limiter.TryCountExport(userId);
            }))
            .ToList();

        startSignal.SetResult();
        var results = await Task.WhenAll(tries);

        Assert.Equal(3, results.Count(passed => passed));
    }
}
