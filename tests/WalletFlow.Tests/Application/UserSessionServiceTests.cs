using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la reconnaissance et la fermeture des sessions, et le nettoyage des sessions et des appareils (GR Auth, SS §3),
// avec de faux outils.
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
            CreatedAt = clock.GetUtcNow().UtcDateTime,
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

    // Se servir de la session sans arrêt ne la prolonge pas au-delà de 12 heures après son ouverture (SS §3).
    [Fact]
    public async Task SessionUsedWithoutPauseClosesTwelveHoursAfterItsOpening()
    {
        AddSession();
        var service = CreateSessionService();

        for (var use = 1; use <= 35; use++)
        {
            clock.Advance(TimeSpan.FromMinutes(20));
            Assert.Equal(OwnerId, await service.FindOwnerAndRecordActivityAsync(SessionToken));
        }

        clock.Advance(TimeSpan.FromMinutes(20));

        Assert.Null(await service.FindOwnerAndRecordActivityAsync(SessionToken));
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
    public async Task CleanupDeletesSessionsInactiveForThirtyMinutesOrOpenForTwelveHours()
    {
        var service = new ExpiredUserSessionsCleanupService(sessionRepository, clock);

        await service.DeleteExpiredSessionsAsync();

        var now = clock.GetUtcNow().UtcDateTime;
        Assert.Equal(now.AddMinutes(-30), Assert.Single(sessionRepository.CleanupDates));
        Assert.Equal(now.AddHours(-12), Assert.Single(sessionRepository.CreationCleanupDates));
    }

    // Le cookie d'appareil dure un an : un appareil inutilisé depuis un an n'a plus de cookie, il est oublié (SS §3).
    [Fact]
    public async Task CleanupForgetsDevicesUnusedForOneYear()
    {
        var service = new ExpiredUserSessionsCleanupService(sessionRepository, clock);

        await service.DeleteForgottenDevicesAsync();

        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddDays(-365), Assert.Single(sessionRepository.DeviceCleanupDates));
    }
}
