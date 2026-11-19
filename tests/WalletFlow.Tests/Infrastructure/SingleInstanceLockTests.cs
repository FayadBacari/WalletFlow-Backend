using static WalletFlow.Tests.Infrastructure.AdvisoryLockTestTools;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le verrou de l'instance unique sur la vraie base : une seule connexion le tient, il se rend aussitôt, et il part avec
// une connexion perdue.
public class SingleInstanceLockTests
{
    [Fact]
    public async Task OnlyOneConnectionHoldsTheLock()
    {
        var key = NewKey();
        await using var first = CreateLock(key);
        await using var second = CreateLock(key);

        Assert.True(await first.TryAcquireAsync());
        Assert.False(await second.TryAcquireAsync());

        Assert.True(await first.IsHeldAsync());
        Assert.False(await second.IsHeldAsync());
        Assert.Equal(first.LastProcessId, await second.FindHolderAsync());
    }

    // Rendu, le verrou se reprend aussitôt : sa connexion n'attend pas, verrou tenu, dans le pool de Npgsql.
    [Fact]
    public async Task ReleasedLockIsFreeAtOnce()
    {
        var key = NewKey();
        await using var first = CreateLock(key);
        await using var second = CreateLock(key);
        Assert.True(await first.TryAcquireAsync());

        await first.ReleaseAsync();

        Assert.Null(await second.FindHolderAsync());
        Assert.True(await second.TryAcquireAsync());
        Assert.False(await first.IsHeldAsync());
    }

    // Une connexion perdue a perdu son verrou : le verrou le voit, et une autre instance peut le prendre.
    [Fact]
    public async Task LostConnectionLosesTheLock()
    {
        var key = NewKey();
        await using var first = CreateLock(key);
        await using var second = CreateLock(key);
        Assert.True(await first.TryAcquireAsync());

        await TerminateHolderAsync(key);

        Assert.False(await first.IsHeldAsync());
        await WaitUntilAsync(() => second.TryAcquireAsync());
        Assert.NotEqual(first.LastProcessId, second.LastProcessId);
    }

    // Deux clés différentes ne se gênent pas : un test ne bloque jamais une API qui tourne sur la même base.
    [Fact]
    public async Task DifferentKeysDoNotBlockEachOther()
    {
        await using var first = CreateLock(NewKey());
        await using var second = CreateLock(NewKey());

        Assert.True(await first.TryAcquireAsync());
        Assert.True(await second.TryAcquireAsync());
    }
}
