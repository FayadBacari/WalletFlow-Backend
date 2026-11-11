using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la reconnaissance et la fermeture des sessions (GR Auth, SS §3), avec de faux outils.
public class UserSessionServiceTests
{
    private const string SessionToken = FakeSecureTokenGenerator.FixedToken;

    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly FakeUserSessionRepository sessionRepository = new FakeUserSessionRepository();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Crée le service à tester, avec ses faux outils.
    private UserSessionService CreateSessionService()
    {
        return new UserSessionService(sessionRepository, new FakeSecureTokenGenerator(), clock);
    }

    // Ajoute à la fausse base une session qui vient de servir.
    private UserSession AddSession()
    {
        var session = new UserSession
        {
            Id = FakeSecureTokenGenerator.FixedHash,
            LastActivityAt = clock.GetUtcNow().UtcDateTime,
            UserId = OwnerId
        };
        sessionRepository.SavedSessions.Add(session);

        return session;
    }

    [Fact]
    public async Task ActiveSessionGivesItsOwnerAndRecordsItsActivity()
    {
        var session = AddSession();
        clock.Advance(TimeSpan.FromMinutes(10));

        var ownerId = await CreateSessionService().FindOwnerAndRecordActivityAsync(SessionToken);

        Assert.Equal(OwnerId, ownerId);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, session.LastActivityAt);
    }

    // Une activité notée il y a moins d'une minute n'est pas réécrite : la base n'est pas chargée à chaque requête.
    [Fact]
    public async Task ActivityRecordedLessThanOneMinuteAgoIsNotWrittenAgain()
    {
        var session = AddSession();
        var lastActivityAt = session.LastActivityAt;
        clock.Advance(TimeSpan.FromSeconds(59));

        var ownerId = await CreateSessionService().FindOwnerAndRecordActivityAsync(SessionToken);

        Assert.Equal(OwnerId, ownerId);
        Assert.Equal(lastActivityAt, session.LastActivityAt);
    }

    [Fact]
    public async Task ActivityRecordedOneMinuteAgoIsWrittenAgain()
    {
        var session = AddSession();
        clock.Advance(TimeSpan.FromMinutes(1));

        await CreateSessionService().FindOwnerAndRecordActivityAsync(SessionToken);

        Assert.Equal(clock.GetUtcNow().UtcDateTime, session.LastActivityAt);
    }

    // Une session expirée ne doit pas pouvoir revivre en notant une nouvelle activité.
    [Fact]
    public async Task ExpiredSessionGivesNoOwnerAndRecordsNoActivity()
    {
        var session = AddSession();
        var lastActivityAt = session.LastActivityAt;
        clock.Advance(TimeSpan.FromMinutes(30));

        var ownerId = await CreateSessionService().FindOwnerAndRecordActivityAsync(SessionToken);

        Assert.Null(ownerId);
        Assert.Equal(lastActivityAt, session.LastActivityAt);
    }

    [Fact]
    public async Task UnknownSessionTokenGivesNoOwner()
    {
        AddSession();

        var ownerId = await CreateSessionService().FindOwnerAndRecordActivityAsync("jeton-de-session-invente");

        Assert.Null(ownerId);
    }

    // Le cookie contient le jeton : la base ne contient que son empreinte. Un jeton égal à l'empreinte lue en base ne vaut rien.
    [Fact]
    public async Task HashReadInTheDatabaseCannotBeUsedAsSessionToken()
    {
        AddSession();

        var ownerId = await CreateSessionService().FindOwnerAndRecordActivityAsync(FakeSecureTokenGenerator.FixedHash);

        Assert.Null(ownerId);
    }

    [Fact]
    public async Task ClosingTheSessionDeletesItFromTheServer()
    {
        AddSession();

        await CreateSessionService().CloseAsync(SessionToken);

        Assert.Empty(sessionRepository.SavedSessions);
    }

    [Fact]
    public async Task ClosingAnUnknownSessionChangesNothing()
    {
        AddSession();

        await CreateSessionService().CloseAsync("jeton-de-session-invente");

        Assert.Single(sessionRepository.SavedSessions);
    }

    [Fact]
    public async Task CleanupDeletesSessionsInactiveForThirtyMinutes()
    {
        var service = new ExpiredUserSessionsCleanupService(sessionRepository, clock);

        await service.DeleteExpiredSessionsAsync();

        var expectedDate = clock.GetUtcNow().UtcDateTime.AddMinutes(-30);
        Assert.Equal(expectedDate, Assert.Single(sessionRepository.CleanupDates));
    }
}
