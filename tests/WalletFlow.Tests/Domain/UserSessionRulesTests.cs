using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie la fermeture des sessions après 30 minutes d'inactivité (GR Auth), et 12 heures après leur ouverture (SS §3).
public class UserSessionRulesTests
{
    private static readonly DateTime LastActivity = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SessionStaysActiveJustBeforeThirtyMinutesOfInactivity()
    {
        var session = new UserSession { CreatedAt = LastActivity, LastActivityAt = LastActivity };

        var justBeforeEnd = LastActivity.AddMinutes(30).AddTicks(-1);

        Assert.True(session.IsActiveAt(justBeforeEnd));
    }

    [Fact]
    public void SessionIsClosedAfterExactlyThirtyMinutesOfInactivity()
    {
        var session = new UserSession { CreatedAt = LastActivity, LastActivityAt = LastActivity };

        var exactEnd = LastActivity.AddMinutes(30);

        Assert.False(session.IsActiveAt(exactEnd));
    }

    // Une session dont on se sert sans arrêt ferme quand même 12 heures après son ouverture : un cookie volé ne sert pas toujours.
    [Fact]
    public void ActiveSessionStaysOpenJustBeforeTwelveHours()
    {
        var session = new UserSession { CreatedAt = LastActivity.AddHours(-12).AddTicks(1), LastActivityAt = LastActivity };

        Assert.True(session.IsActiveAt(LastActivity));
    }

    [Fact]
    public void ActiveSessionIsClosedTwelveHoursAfterItsOpening()
    {
        var session = new UserSession { CreatedAt = LastActivity.AddHours(-12), LastActivityAt = LastActivity };

        Assert.False(session.IsActiveAt(LastActivity));
    }

    [Fact]
    public void ActivityIsRecordedAgainOnlyAfterOneMinute()
    {
        var session = new UserSession { CreatedAt = LastActivity, LastActivityAt = LastActivity };

        Assert.False(session.ShouldRecordActivityAt(LastActivity.AddMinutes(1).AddTicks(-1)));
        Assert.True(session.ShouldRecordActivityAt(LastActivity.AddMinutes(1)));
    }

    // Deux requêtes peuvent noter leur activité dans le désordre : la date de dernière activité ne recule jamais.
    [Fact]
    public void LastActivityNeverGoesBackInTime()
    {
        var session = new UserSession { CreatedAt = LastActivity, LastActivityAt = LastActivity };

        session.RecordActivity(LastActivity.AddMinutes(-5));
        Assert.Equal(LastActivity, session.LastActivityAt);

        session.RecordActivity(LastActivity.AddMinutes(5));
        Assert.Equal(LastActivity.AddMinutes(5), session.LastActivityAt);
    }
}
