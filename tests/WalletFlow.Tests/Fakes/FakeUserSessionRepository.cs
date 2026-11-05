using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les sessions et les appareils en mémoire.
public class FakeUserSessionRepository : IUserSessionRepository
{
    public const string TechnicalDetailOfTheSimulatedSessionFailure = "detail-technique-de-la-panne-des-sessions";

    private readonly FakeSecurityEventRepository repositoryThatKeepsTheSecurityEventsInMemory;

    public List<UserSession> SavedSessions { get; } = new List<UserSession>();
    public List<Device> SavedDevices { get; } = new List<Device>();
    public List<DateTime> DatesOfTheCleanupsRequested { get; } = new List<DateTime>();
    public int NumberOfSearchesOfSessionsInTheDatabase { get; private set; } = 0;

    // Simule d'autres requêtes qui changent le compte juste avant l'enregistrement de la connexion.
    public int NumberOfConflictsBeforeTheLoginIsSaved { get; set; } = 0;

    // Simule une panne de la base : enregistrer une connexion ou chercher une session échoue.
    public bool DatabaseIsDownDuringThisTest { get; set; } = false;

    // Simule une panne de la base au moment de fermer toutes les sessions d'un compte.
    public bool DatabaseIsDownWhileClosingEverySession { get; set; } = false;

    // Simule une panne de la base pendant l'effacement des sessions expirées.
    public bool DatabaseIsDownDuringTheCleanup { get; set; } = false;

    // Reçoit le faux journal, où la connexion réussie écrit aussi son événement.
    public FakeUserSessionRepository(FakeSecurityEventRepository repositoryThatKeepsTheSecurityEventsInMemory)
    {
        this.repositoryThatKeepsTheSecurityEventsInMemory = repositoryThatKeepsTheSecurityEventsInMemory;
    }

    // Garde tout en mémoire, sauf si un conflit est simulé : rien n'est alors gardé, comme avec la vraie transaction.
    public Task<bool> TryToSaveSuccessfulLoginOfThisAccountAsync(
        User accountThatHasJustLoggedIn,
        UserSession newSessionOfTheUserAccount,
        Device? newDeviceOfTheUserAccount,
        SecurityEvent securityEventOfTheSuccessfulLogin)
    {
        if (DatabaseIsDownDuringThisTest)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedSessionFailure);
        }

        if (NumberOfConflictsBeforeTheLoginIsSaved > 0)
        {
            NumberOfConflictsBeforeTheLoginIsSaved = NumberOfConflictsBeforeTheLoginIsSaved - 1;
            return Task.FromResult(false);
        }

        SavedSessions.Add(newSessionOfTheUserAccount);
        repositoryThatKeepsTheSecurityEventsInMemory.SavedSecurityEvents.Add(securityEventOfTheSuccessfulLogin);

        if (newDeviceOfTheUserAccount != null)
        {
            SavedDevices.Add(newDeviceOfTheUserAccount);
        }

        return Task.FromResult(true);
    }

    // Cherche en mémoire cette empreinte de jeton d'appareil, pour ce compte.
    public Task<bool> CheckThatDeviceTokenBelongsToThisAccountAsync(string hashOfTheDeviceTokenFromCookie, Guid identifierOfTheAccountLoggingIn)
    {
        foreach (var deviceAlreadyKeptInThisMemoryList in SavedDevices)
        {
            if (deviceAlreadyKeptInThisMemoryList.DeviceToken == hashOfTheDeviceTokenFromCookie && deviceAlreadyKeptInThisMemoryList.UserId == identifierOfTheAccountLoggingIn)
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    // Cherche en mémoire une session par son empreinte.
    public Task<UserSession?> FindUserSessionByItsHashAsync(string hashOfTheTokenOfTheSession)
    {
        if (DatabaseIsDownDuringThisTest)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedSessionFailure);
        }

        NumberOfSearchesOfSessionsInTheDatabase = NumberOfSearchesOfSessionsInTheDatabase + 1;

        foreach (var sessionAlreadyKeptInThisMemoryList in SavedSessions)
        {
            if (sessionAlreadyKeptInThisMemoryList.Id == hashOfTheTokenOfTheSession)
            {
                return Task.FromResult<UserSession?>(sessionAlreadyKeptInThisMemoryList);
            }
        }

        return Task.FromResult<UserSession?>(null);
    }

    // Change la date de dernière activité de la session gardée en mémoire.
    public Task RecordActivityOfThisUserSessionAsync(string hashOfTheTokenOfTheSession, DateTime dateOfTheCurrentHttpRequest)
    {
        foreach (var sessionAlreadyKeptInThisMemoryList in SavedSessions)
        {
            if (sessionAlreadyKeptInThisMemoryList.Id == hashOfTheTokenOfTheSession)
            {
                sessionAlreadyKeptInThisMemoryList.LastActivityAt = dateOfTheCurrentHttpRequest;
            }
        }

        return Task.CompletedTask;
    }

    // Retire la session de la liste.
    public Task DeleteUserSessionByItsHashAsync(string hashOfTheTokenOfTheSession)
    {
        SavedSessions.RemoveAll(sessionAlreadyKeptInThisMemoryList => sessionAlreadyKeptInThisMemoryList.Id == hashOfTheTokenOfTheSession);

        return Task.CompletedTask;
    }

    // Retire de la liste toutes les sessions du compte.
    public Task DeleteAllUserSessionsOfThisAccountAsync(Guid identifierOfTheAccountToDisconnectEverywhere)
    {
        if (DatabaseIsDownWhileClosingEverySession)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedSessionFailure);
        }

        SavedSessions.RemoveAll(sessionAlreadyKeptInThisMemoryList => sessionAlreadyKeptInThisMemoryList.UserId == identifierOfTheAccountToDisconnectEverywhere);

        return Task.CompletedTask;
    }

    // Garde la date du passage. La vraie règle d'effacement est testée sur la vraie base.
    public Task<int> DeleteUserSessionsInactiveSinceThisDateAsync(DateTime oldestDateOfActivityStillAccepted)
    {
        if (DatabaseIsDownDuringTheCleanup)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedSessionFailure);
        }

        DatesOfTheCleanupsRequested.Add(oldestDateOfActivityStillAccepted);
        return Task.FromResult(0);
    }
}
