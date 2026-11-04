using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la reconnaissance et la fermeture des sessions (GR Auth, SS §3), avec de faux outils.
public class UserSessionServiceTests
{
    private const string SessionTokenReadInTheCookieInTheseTests = FakeSecureTokenGenerator.TokenAlwaysReturnedByThisFakeGenerator;

    private static readonly Guid IdentifierOfTheSessionOwnerInTheseTests = Guid.NewGuid();

    private readonly FakeUserSessionRepository repositoryThatKeepsSessionsInMemory = new FakeUserSessionRepository(new FakeSecurityEventRepository());
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Crée le service à tester, avec ses faux outils.
    private UserSessionService CreateSessionServiceWithFakeTools()
    {
        return new UserSessionService(repositoryThatKeepsSessionsInMemory, new FakeSecureTokenGenerator(), clockThatOnlyMovesWhenTestsAskIt);
    }

    // Ajoute à la fausse base une session qui vient de servir.
    private UserSession AddSessionToTheFakeDatabase()
    {
        var sessionAddedToTheFakeDatabase = new UserSession
        {
            Id = FakeSecureTokenGenerator.HashAlwaysReturnedByThisFakeGenerator,
            LastActivityAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime,
            UserId = IdentifierOfTheSessionOwnerInTheseTests
        };
        repositoryThatKeepsSessionsInMemory.SavedSessions.Add(sessionAddedToTheFakeDatabase);

        return sessionAddedToTheFakeDatabase;
    }

    [Fact]
    public async Task ActiveSessionGivesItsOwnerAndRecordsItsActivity()
    {
        var sessionBeingTestedInThisRequest = AddSessionToTheFakeDatabase();
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(10));

        var identifierOfTheOwnerOfTheSession = await CreateSessionServiceWithFakeTools().FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(SessionTokenReadInTheCookieInTheseTests);

        Assert.Equal(IdentifierOfTheSessionOwnerInTheseTests, identifierOfTheOwnerOfTheSession);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, sessionBeingTestedInThisRequest.LastActivityAt);
    }

    // Une session expirée ne doit pas pouvoir revivre en notant une nouvelle activité.
    [Fact]
    public async Task ExpiredSessionGivesNoOwnerAndRecordsNoActivity()
    {
        var sessionBeingTestedInThisRequest = AddSessionToTheFakeDatabase();
        var dateOfTheLastActivityOfTheSession = sessionBeingTestedInThisRequest.LastActivityAt;
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(30));

        var identifierOfTheOwnerOfTheSession = await CreateSessionServiceWithFakeTools().FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(SessionTokenReadInTheCookieInTheseTests);

        Assert.Null(identifierOfTheOwnerOfTheSession);
        Assert.Equal(dateOfTheLastActivityOfTheSession, sessionBeingTestedInThisRequest.LastActivityAt);
    }

    [Fact]
    public async Task UnknownSessionTokenGivesNoOwner()
    {
        AddSessionToTheFakeDatabase();

        var identifierOfTheOwnerOfTheSession = await CreateSessionServiceWithFakeTools().FindOwnerOfTheActiveSessionThenRecordItsActivityAsync("jeton-de-session-invente");

        Assert.Null(identifierOfTheOwnerOfTheSession);
    }

    // Le cookie contient le jeton : la base ne contient que son empreinte. Un jeton égal à l'empreinte lue en base ne vaut rien.
    [Fact]
    public async Task HashReadInTheDatabaseCannotBeUsedAsSessionToken()
    {
        AddSessionToTheFakeDatabase();

        var identifierOfTheOwnerOfTheSession = await CreateSessionServiceWithFakeTools().FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(FakeSecureTokenGenerator.HashAlwaysReturnedByThisFakeGenerator);

        Assert.Null(identifierOfTheOwnerOfTheSession);
    }

    [Fact]
    public async Task ClosingTheSessionDeletesItFromTheServer()
    {
        AddSessionToTheFakeDatabase();

        await CreateSessionServiceWithFakeTools().CloseUserSessionOfThisCookieAsync(SessionTokenReadInTheCookieInTheseTests);

        Assert.Empty(repositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    [Fact]
    public async Task ClosingAnUnknownSessionChangesNothing()
    {
        AddSessionToTheFakeDatabase();

        await CreateSessionServiceWithFakeTools().CloseUserSessionOfThisCookieAsync("jeton-de-session-invente");

        Assert.Single(repositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    [Fact]
    public async Task CleanupDeletesSessionsInactiveForThirtyMinutes()
    {
        var serviceBeingTestedWithFakeTools = new ExpiredUserSessionsCleanupService(repositoryThatKeepsSessionsInMemory, clockThatOnlyMovesWhenTestsAskIt);

        await serviceBeingTestedWithFakeTools.DeleteExpiredUserSessionsFromTheDatabaseAsync();

        var dateExpectedForTheSessionsCleanup = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddMinutes(-30);
        Assert.Equal(dateExpectedForTheSessionsCleanup, Assert.Single(repositoryThatKeepsSessionsInMemory.DatesOfTheCleanupsRequested));
    }
}
