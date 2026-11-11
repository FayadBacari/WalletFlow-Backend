using WalletFlow.Application.UserAccounts;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le cas d'utilisation de l'effacement. La vraie règle est testée sur la vraie base.
public class UnconfirmedAccountsCleanupServiceTests
{
    [Fact]
    public async Task CleanupUsesTheCurrentDateOfTheClock()
    {
        var accountRepository = new FakeUserAccountRepository();
        var clock = new FakeTimeProvider();
        var service = new UnconfirmedAccountsCleanupService(accountRepository, clock);

        await service.DeleteExpiredUnconfirmedAccountsAsync();

        Assert.Equal(clock.GetUtcNow().UtcDateTime, Assert.Single(accountRepository.CleanupDates));
    }
}
