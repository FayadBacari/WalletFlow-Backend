using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie la fermeture des sessions après 30 minutes d'inactivité (GR Auth, SS §3).
public class UserSessionRulesTests
{
    private static readonly DateTime LastActivity = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SessionStaysActiveJustBeforeThirtyMinutesOfInactivity()
    {
        var session = new UserSession { LastActivityAt = LastActivity };

        var justBeforeEnd = LastActivity.AddMinutes(30).AddTicks(-1);

        Assert.True(session.IsActiveAt(justBeforeEnd));
    }

    [Fact]
    public void SessionIsClosedAfterExactlyThirtyMinutesOfInactivity()
    {
        var session = new UserSession { LastActivityAt = LastActivity };

        var exactEnd = LastActivity.AddMinutes(30);

        Assert.False(session.IsActiveAt(exactEnd));
    }

    [Fact]
    public void ActivityIsRecordedAgainOnlyAfterOneMinute()
    {
        var session = new UserSession { LastActivityAt = LastActivity };

        Assert.False(session.ShouldRecordActivityAt(LastActivity.AddMinutes(1).AddTicks(-1)));
        Assert.True(session.ShouldRecordActivityAt(LastActivity.AddMinutes(1)));
    }

    // Deux requêtes peuvent noter leur activité dans le désordre : la date de dernière activité ne recule jamais.
    [Fact]
    public void LastActivityNeverGoesBackInTime()
    {
        var session = new UserSession { LastActivityAt = LastActivity };

        session.RecordActivity(LastActivity.AddMinutes(-5));
        Assert.Equal(LastActivity, session.LastActivityAt);

        session.RecordActivity(LastActivity.AddMinutes(5));
        Assert.Equal(LastActivity.AddMinutes(5), session.LastActivityAt);
    }
}
