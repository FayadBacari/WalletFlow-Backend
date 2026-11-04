using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie la fermeture des sessions après 30 minutes d'inactivité (GR Auth, SS §3).
public class UserSessionRulesTests
{
    private static readonly DateTime DateOfTheLastActivityInTheseTests = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SessionStaysActiveJustBeforeThirtyMinutesOfInactivity()
    {
        var sessionBeingTestedForItsInactivity = new UserSession { LastActivityAt = DateOfTheLastActivityInTheseTests };

        var dateJustBeforeTheEndOfTheSession = DateOfTheLastActivityInTheseTests.AddMinutes(30).AddTicks(-1);

        Assert.True(UserSessionRules.CheckThatSessionIsStillActiveAtThisDate(sessionBeingTestedForItsInactivity, dateJustBeforeTheEndOfTheSession));
    }

    [Fact]
    public void SessionIsClosedAfterExactlyThirtyMinutesOfInactivity()
    {
        var sessionBeingTestedForItsInactivity = new UserSession { LastActivityAt = DateOfTheLastActivityInTheseTests };

        var dateOfTheExactEndOfTheSession = DateOfTheLastActivityInTheseTests.AddMinutes(30);

        Assert.False(UserSessionRules.CheckThatSessionIsStillActiveAtThisDate(sessionBeingTestedForItsInactivity, dateOfTheExactEndOfTheSession));
    }
}
