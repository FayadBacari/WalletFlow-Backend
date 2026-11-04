using WalletFlow.Application.UserAccounts;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le cas d'utilisation de l'effacement. La vraie règle est testée sur la vraie base.
public class UnconfirmedAccountsCleanupServiceTests
{
    [Fact]
    public async Task CleanupUsesTheCurrentDateOfTheClock()
    {
        var repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository();
        var clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();
        var serviceBeingTestedWithFakeTools = new UnconfirmedAccountsCleanupService(repositoryThatKeepsAccountsInMemory, clockThatOnlyMovesWhenTestsAskIt);

        await serviceBeingTestedWithFakeTools.DeleteUnconfirmedAccountsWithExpiredLinkAsync();

        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, Assert.Single(repositoryThatKeepsAccountsInMemory.DatesOfTheCleanupsRequested));
    }
}
