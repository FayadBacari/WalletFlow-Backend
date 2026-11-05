using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les sessions et les appareils dans la base PostgreSQL.
public class UserSessionRepository : IUserSessionRepository
{
    private readonly WalletFlowDatabaseContext databaseContextOfTheCurrentHttpRequest;

    // Reçoit l'accès à la base de la requête en cours.
    public UserSessionRepository(WalletFlowDatabaseContext databaseContextOfTheCurrentHttpRequest)
    {
        this.databaseContextOfTheCurrentHttpRequest = databaseContextOfTheCurrentHttpRequest;
    }

    // Enregistre tout en une seule transaction SQL : SaveChangesAsync enregistre tout, ou rien.
    // Renvoie faux si une autre requête vient de modifier le compte : rien n'est alors enregistré.
    public async Task<bool> TryToSaveSuccessfulLoginOfThisAccountAsync(
        User accountThatHasJustLoggedIn,
        UserSession newSessionOfTheUserAccount,
        Device? newDeviceOfTheUserAccount,
        BackupCode? backupCodeUsedForThisLogin,
        SecurityEvent securityEventOfTheSuccessfulLogin)
    {
        // EF Core suit le compte depuis sa lecture. Le compte est écrit même si ses compteurs n'ont pas changé : cette écriture
        // contrôle en base que le mot de passe vérifié est toujours le bon. Sinon, une session survivrait à une récupération.
        databaseContextOfTheCurrentHttpRequest.Entry(accountThatHasJustLoggedIn).Property(nameof(User.FailedLoginAttemptCount)).IsModified = true;

        databaseContextOfTheCurrentHttpRequest.UserSessions.Add(newSessionOfTheUserAccount);
        databaseContextOfTheCurrentHttpRequest.SecurityEvents.Add(securityEventOfTheSuccessfulLogin);

        if (newDeviceOfTheUserAccount != null)
        {
            databaseContextOfTheCurrentHttpRequest.Devices.Add(newDeviceOfTheUserAccount);
        }

        // EF Core suit ce code depuis sa lecture. Le contrôle sur sa date d'utilisation refuse tout si une autre requête vient de s'en servir.
        if (backupCodeUsedForThisLogin != null)
        {
            backupCodeUsedForThisLogin.UsedAt = newSessionOfTheUserAccount.LastActivityAt;
        }

        return await databaseContextOfTheCurrentHttpRequest.TryToSaveChangesUnlessAnotherRequestChangedTheSameRowsAsync();
    }

    // Renvoie vrai si cette empreinte de jeton d'appareil appartient à ce compte.
    public async Task<bool> CheckThatDeviceTokenBelongsToThisAccountAsync(string hashOfTheDeviceTokenFromCookie, Guid identifierOfTheAccountLoggingIn)
    {
        return await databaseContextOfTheCurrentHttpRequest.Devices.AnyAsync(deviceAlreadySavedInTheDatabase =>
            deviceAlreadySavedInTheDatabase.DeviceToken == hashOfTheDeviceTokenFromCookie
            && deviceAlreadySavedInTheDatabase.UserId == identifierOfTheAccountLoggingIn);
    }

    // Cherche une session par l'empreinte de son identifiant. Renvoie null si elle n'existe pas.
    public async Task<UserSession?> FindUserSessionByItsHashAsync(string hashOfTheTokenOfTheSession)
    {
        return await databaseContextOfTheCurrentHttpRequest.UserSessions.AsNoTracking().SingleOrDefaultAsync(sessionAlreadySavedInTheDatabase =>
            sessionAlreadySavedInTheDatabase.Id == hashOfTheTokenOfTheSession);
    }

    // Note que la session vient de servir.
    public async Task RecordActivityOfThisUserSessionAsync(string hashOfTheTokenOfTheSession, DateTime dateOfTheCurrentHttpRequest)
    {
        await databaseContextOfTheCurrentHttpRequest.UserSessions
            .Where(sessionAlreadySavedInTheDatabase => sessionAlreadySavedInTheDatabase.Id == hashOfTheTokenOfTheSession)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheSession => setterOfTheColumnsOfTheSession.SetProperty(
                sessionBeingUpdatedInTheDatabase => sessionBeingUpdatedInTheDatabase.LastActivityAt,
                dateOfTheCurrentHttpRequest));
    }

    // Détruit une session. Ne fait rien si elle n'existe plus.
    public async Task DeleteUserSessionByItsHashAsync(string hashOfTheTokenOfTheSession)
    {
        await databaseContextOfTheCurrentHttpRequest.UserSessions
            .Where(sessionAlreadySavedInTheDatabase => sessionAlreadySavedInTheDatabase.Id == hashOfTheTokenOfTheSession)
            .ExecuteDeleteAsync();
    }

    // Détruit toutes les sessions d'un compte, après une récupération de mot de passe (SS §3).
    public async Task DeleteAllUserSessionsOfThisAccountAsync(Guid identifierOfTheAccountToDisconnectEverywhere)
    {
        await databaseContextOfTheCurrentHttpRequest.UserSessions
            .Where(sessionAlreadySavedInTheDatabase => sessionAlreadySavedInTheDatabase.UserId == identifierOfTheAccountToDisconnectEverywhere)
            .ExecuteDeleteAsync();
    }

    // Efface les sessions sans activité depuis cette date, et renvoie leur nombre.
    public async Task<int> DeleteUserSessionsInactiveSinceThisDateAsync(DateTime oldestDateOfActivityStillAccepted)
    {
        return await databaseContextOfTheCurrentHttpRequest.UserSessions
            .Where(sessionAlreadySavedInTheDatabase => sessionAlreadySavedInTheDatabase.LastActivityAt <= oldestDateOfActivityStillAccepted)
            .ExecuteDeleteAsync();
    }
}
