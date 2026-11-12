using WalletFlow.Application.BankConnections;
using WalletFlow.Infrastructure.BankData;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les mémoires des connexions bancaires : les autorisations commencées (SS §7), la limite des mises à jour
// demandées par l'utilisateur (SS §6), et l'horaire des mises à jour en arrière-plan (GR Comptes).
public class BankMemoryStoresTests
{
    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly Guid userId = Guid.NewGuid();

    // Crée une autorisation commencée par cet utilisateur.
    private static PendingBankAuthorization CreateAuthorization(Guid userId)
    {
        return new PendingBankAuthorization(userId, "Banque de test", null);
    }

    // SS §7 : un state ne sert qu'une fois.
    [Fact]
    public void AuthorizationIsTakenOnlyOnce()
    {
        var store = new InMemoryBankAuthorizationStore(clock);
        var authorization = CreateAuthorization(userId);
        store.Save("empreinte", authorization);

        Assert.Equal(authorization, store.TryTake("empreinte", userId));
        Assert.Null(store.TryTake("empreinte", userId));
    }

    // SS §7 : le state d'un autre compte est refusé, et oublié : un lien piégé ne peut plus servir à son auteur.
    [Fact]
    public void AuthorizationOfAnotherUserIsRefusedAndForgotten()
    {
        var store = new InMemoryBankAuthorizationStore(clock);
        store.Save("empreinte", CreateAuthorization(userId));

        Assert.Null(store.TryTake("empreinte", Guid.NewGuid()));
        Assert.Null(store.TryTake("empreinte", userId));
    }

    [Fact]
    public void AuthorizationExpiresAfterFifteenMinutes()
    {
        var store = new InMemoryBankAuthorizationStore(clock);
        store.Save("encore-bonne", CreateAuthorization(userId));
        store.Save("expiree", CreateAuthorization(userId));

        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        Assert.NotNull(store.TryTake("encore-bonne", userId));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(store.TryTake("expiree", userId));
    }

    // Un utilisateur ne peut pas remplir la mémoire : sa plus ancienne autorisation part quand il en commence une sixième.
    [Fact]
    public void UserKeepsAtMostFivePendingAuthorizations()
    {
        var store = new InMemoryBankAuthorizationStore(clock);

        for (var number = 1; number <= 6; number++)
        {
            store.Save("empreinte-" + number, CreateAuthorization(userId));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(5, store.PendingCount);
        Assert.Null(store.TryTake("empreinte-1", userId));
        Assert.NotNull(store.TryTake("empreinte-6", userId));
    }

    // Les autorisations expirées sont oubliées : la mémoire ne grossit pas sans fin.
    [Fact]
    public void ExpiredAuthorizationsAreForgotten()
    {
        var store = new InMemoryBankAuthorizationStore(clock);
        store.Save("vieille", CreateAuthorization(Guid.NewGuid()));

        clock.Advance(TimeSpan.FromMinutes(16));
        store.Save("nouvelle", CreateAuthorization(userId));

        Assert.Equal(1, store.PendingCount);
    }

    // SS §6 : une mise à jour par minute et par utilisateur.
    [Fact]
    public void UserCanUpdateOncePerMinute()
    {
        var limiter = new InMemoryBankUpdateLimiter(clock);
        var otherUserId = Guid.NewGuid();

        Assert.True(limiter.TryCountUpdate(userId));
        Assert.False(limiter.TryCountUpdate(userId));
        Assert.True(limiter.TryCountUpdate(otherUserId));

        clock.Advance(TimeSpan.FromMinutes(1) - TimeSpan.FromSeconds(1));
        Assert.False(limiter.TryCountUpdate(userId));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryCountUpdate(userId));
    }

    // GR Comptes : 4 mises à jour par jour au plus en arrière-plan, soit une toutes les 6 heures pour une connexion.
    [Fact]
    public void ConnectionIsUpdatedInTheBackgroundEverySixHours()
    {
        var schedule = new InMemoryBankUpdateSchedule(clock);
        var connectionId = Guid.NewGuid();
        var otherConnectionId = Guid.NewGuid();
        var updateCount = 0;

        for (var minute = 0; minute < 24 * 60; minute = minute + 15)
        {
            if (schedule.TryStartBackgroundUpdate(connectionId))
            {
                updateCount = updateCount + 1;
            }

            clock.Advance(TimeSpan.FromMinutes(15));
        }

        Assert.Equal(4, updateCount);
        Assert.True(schedule.TryStartBackgroundUpdate(otherConnectionId));
    }
}
