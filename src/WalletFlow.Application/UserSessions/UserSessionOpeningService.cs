using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Ouvre la session d'une connexion réussie, avec ou sans double authentification (GR Auth, SS §3).
public class UserSessionOpeningService
{
    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly IUserSessionRepository repositoryOfTheUserSessionsInDatabase;
    private readonly ITwoFactorAuthenticationRepository repositoryOfTheBackupCodesInDatabase;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomTokens;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont l'ouverture d'une session a besoin.
    public UserSessionOpeningService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        IUserSessionRepository repositoryOfTheUserSessionsInDatabase,
        ITwoFactorAuthenticationRepository repositoryOfTheBackupCodesInDatabase,
        ISecureTokenGenerator generatorOfTheSecureRandomTokens,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.repositoryOfTheUserSessionsInDatabase = repositoryOfTheUserSessionsInDatabase;
        this.repositoryOfTheBackupCodesInDatabase = repositoryOfTheBackupCodesInDatabase;
        this.generatorOfTheSecureRandomTokens = generatorOfTheSecureRandomTokens;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Ouvre une session avec un nouvel identifiant (SS §3), et enregistre l'appareil s'il est nouveau.
    // Renvoie null si la connexion ne peut plus s'enregistrer : rien n'est alors ouvert.
    public async Task<OpenedUserSession?> TryToOpenSessionForThisSuccessfulLoginAsync(SuccessfulLoginToSave successfulLoginToSaveInTheDatabase)
    {
        var generatedTokenOfTheNewSession = generatorOfTheSecureRandomTokens.GenerateNewRandomSecureTokenWithItsHash();
        var generatedTokenOfTheNewDevice = await GenerateDeviceTokenIfThisDeviceIsNewForTheAccountAsync(successfulLoginToSaveInTheDatabase.IdentifierOfTheAccountLoggingIn, successfulLoginToSaveInTheDatabase.DeviceTokenReadInTheCookie);

        var loginWasSavedInTheDatabase = await TryToSaveLoginUnlessOtherRequestsKeepChangingTheAccountAsync(
            successfulLoginToSaveInTheDatabase,
            CreateSessionFromThisGeneratedToken(generatedTokenOfTheNewSession, successfulLoginToSaveInTheDatabase.IdentifierOfTheAccountLoggingIn),
            CreateDeviceFromThisGeneratedToken(generatedTokenOfTheNewDevice, successfulLoginToSaveInTheDatabase.IdentifierOfTheAccountLoggingIn));

        if (!loginWasSavedInTheDatabase)
        {
            return null;
        }

        await CloseOldSessionOfThisBrowserAsync(successfulLoginToSaveInTheDatabase.SessionTokenReadInTheCookie);

        // L'appareil garde son jeton s'il est déjà connu du compte.
        return new OpenedUserSession
        {
            SessionTokenToPutInTheCookie = generatedTokenOfTheNewSession.TokenGivenOnlyToTheVisitor,
            DeviceTokenToPutInTheCookie = generatedTokenOfTheNewDevice?.TokenGivenOnlyToTheVisitor ?? successfulLoginToSaveInTheDatabase.DeviceTokenReadInTheCookie!
        };
    }

    // Enregistre la connexion. Si une autre requête a changé le compte pendant les vérifications, relit le compte et recommence.
    // Renvoie faux si le compte a disparu, s'il change encore au bout de 10 passages, ou si ce qui vient d'être vérifié n'est plus valable.
    private async Task<bool> TryToSaveLoginUnlessOtherRequestsKeepChangingTheAccountAsync(
        SuccessfulLoginToSave successfulLoginToSaveInTheDatabase,
        UserSession newSessionOfTheUserAccount,
        Device? newDeviceOfTheUserAccount)
    {
        var securityEventOfTheSuccessfulLogin = CreateSecurityEventOfTheSuccessfulLogin(successfulLoginToSaveInTheDatabase);

        for (var numberOfTheTryBeingDone = 1; numberOfTheTryBeingDone <= AccountLockoutRules.MaximumNumberOfTriesWhileOtherRequestsChangeTheAccount; numberOfTheTryBeingDone++)
        {
            var accountReadBeforeSavingTheLogin = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(successfulLoginToSaveInTheDatabase.IdentifierOfTheAccountLoggingIn);
            if (!CheckThatAccountStillMatchesWhatWasJustChecked(accountReadBeforeSavingTheLogin, successfulLoginToSaveInTheDatabase))
            {
                return false;
            }

            BackupCode? backupCodeUsedForThisLogin = null;
            if (successfulLoginToSaveInTheDatabase.IdentifierOfTheBackupCodeJustUsed != null)
            {
                backupCodeUsedForThisLogin = await repositoryOfTheBackupCodesInDatabase.FindBackupCodeByItsIdentifierAsync(successfulLoginToSaveInTheDatabase.IdentifierOfTheBackupCodeJustUsed.Value);
                if (backupCodeUsedForThisLogin == null || backupCodeUsedForThisLogin.UsedAt != null)
                {
                    return false;
                }
            }

            AccountLockoutRules.ResetLoginCountersAfterSuccessfulLogin(accountReadBeforeSavingTheLogin!);

            if (await repositoryOfTheUserSessionsInDatabase.TryToSaveSuccessfulLoginOfThisAccountAsync(accountReadBeforeSavingTheLogin!, newSessionOfTheUserAccount, newDeviceOfTheUserAccount, backupCodeUsedForThisLogin, securityEventOfTheSuccessfulLogin))
            {
                return true;
            }
        }

        return false;
    }

    // Une récupération du mot de passe (SS §3), une activation ou une désactivation a pu passer pendant les vérifications.
    // Une connexion sans code ne s'enregistre donc jamais sur un compte qui vient d'activer la double authentification.
    private static bool CheckThatAccountStillMatchesWhatWasJustChecked(User? accountReadBeforeSavingTheLogin, SuccessfulLoginToSave successfulLoginToSaveInTheDatabase)
    {
        return accountReadBeforeSavingTheLogin != null
            && accountReadBeforeSavingTheLogin.PasswordHash == successfulLoginToSaveInTheDatabase.SecureHashOfThePasswordJustChecked
            && accountReadBeforeSavingTheLogin.EncryptedTotpSecret == successfulLoginToSaveInTheDatabase.EncryptedTotpSecretJustChecked;
    }

    // Crée la session à enregistrer. Seule l'empreinte de son identifiant va en base.
    private UserSession CreateSessionFromThisGeneratedToken(GeneratedSecureToken generatedTokenOfTheNewSession, Guid identifierOfTheAccountLoggingIn)
    {
        return new UserSession
        {
            Id = generatedTokenOfTheNewSession.HashToStoreInTheDatabase,
            LastActivityAt = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime,
            UserId = identifierOfTheAccountLoggingIn
        };
    }

    // Tire un nouveau jeton si le navigateur n'a pas de jeton d'appareil, ou s'il n'est pas celui de ce compte.
    // Renvoie null si l'appareil est déjà connu du compte.
    private async Task<GeneratedSecureToken?> GenerateDeviceTokenIfThisDeviceIsNewForTheAccountAsync(Guid identifierOfTheAccountLoggingIn, string? deviceTokenReadInTheCookie)
    {
        if (deviceTokenReadInTheCookie != null)
        {
            var hashOfTheDeviceTokenFromCookie = generatorOfTheSecureRandomTokens.ConvertSecureTokenIntoItsHash(deviceTokenReadInTheCookie);

            if (await repositoryOfTheUserSessionsInDatabase.CheckThatDeviceTokenBelongsToThisAccountAsync(hashOfTheDeviceTokenFromCookie, identifierOfTheAccountLoggingIn))
            {
                return null;
            }
        }

        return generatorOfTheSecureRandomTokens.GenerateNewRandomSecureTokenWithItsHash();
    }

    // Crée l'appareil à enregistrer. Seule l'empreinte de son jeton va en base.
    private static Device? CreateDeviceFromThisGeneratedToken(GeneratedSecureToken? generatedTokenOfTheNewDevice, Guid identifierOfTheAccountLoggingIn)
    {
        if (generatedTokenOfTheNewDevice == null)
        {
            return null;
        }

        return new Device { DeviceToken = generatedTokenOfTheNewDevice.HashToStoreInTheDatabase, UserId = identifierOfTheAccountLoggingIn };
    }

    // Le navigateur reçoit une nouvelle session : l'ancienne ne doit pas rester ouverte sans que personne ne s'en serve.
    private async Task CloseOldSessionOfThisBrowserAsync(string? sessionTokenReadInTheCookie)
    {
        if (sessionTokenReadInTheCookie == null)
        {
            return;
        }

        var hashOfTheOldSessionToken = generatorOfTheSecureRandomTokens.ConvertSecureTokenIntoItsHash(sessionTokenReadInTheCookie);
        await repositoryOfTheUserSessionsInDatabase.DeleteUserSessionByItsHashAsync(hashOfTheOldSessionToken);
    }

    // Crée l'événement du journal : date, utilisateur, adresse IP, événement et résultat. Jamais l'adresse e-mail (SS §7 et §11).
    private SecurityEvent CreateSecurityEventOfTheSuccessfulLogin(SuccessfulLoginToSave successfulLoginToSaveInTheDatabase)
    {
        return new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Success,
            OccurredAt = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime,
            IpAddress = successfulLoginToSaveInTheDatabase.IpAddressOfTheVisitor,
            UserId = successfulLoginToSaveInTheDatabase.IdentifierOfTheAccountLoggingIn
        };
    }
}
