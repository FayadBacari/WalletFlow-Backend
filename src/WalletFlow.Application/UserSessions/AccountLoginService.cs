using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Cas d'utilisation : un visiteur se connecte avec son adresse e-mail et son mot de passe (GR Auth, SS §2 et §3).
public class AccountLoginService
{
    public const string ErrorMessageForWrongLoginCredentials = "Identifiants incorrects";

    public const string ErrorMessageForEmailAddressNotConfirmed =
        "Ton adresse e-mail n'est pas encore confirmée. Ouvre le lien reçu par e-mail, ou inscris-toi à nouveau pour en recevoir un autre.";

    // Un conflit veut dire qu'une autre requête a réussi à écrire. Le compte se bloque au bout de 5 essais :
    // 10 passages suffisent donc largement. Au-delà, la connexion est refusée, jamais acceptée sans contrôle.
    private const int MaximumNumberOfTriesToSaveTheLoginOfOneAccount = 10;

    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly IUserSessionRepository repositoryOfTheUserSessionsInDatabase;
    private readonly ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase;
    private readonly IPasswordHasher hasherOfThePasswordsTypedByVisitors;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomTokens;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont la connexion a besoin.
    public AccountLoginService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        IUserSessionRepository repositoryOfTheUserSessionsInDatabase,
        ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase,
        IPasswordHasher hasherOfThePasswordsTypedByVisitors,
        ISecureTokenGenerator generatorOfTheSecureRandomTokens,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.repositoryOfTheUserSessionsInDatabase = repositoryOfTheUserSessionsInDatabase;
        this.repositoryOfTheSecurityEventsInDatabase = repositoryOfTheSecurityEventsInDatabase;
        this.hasherOfThePasswordsTypedByVisitors = hasherOfThePasswordsTypedByVisitors;
        this.generatorOfTheSecureRandomTokens = generatorOfTheSecureRandomTokens;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Connecte un visiteur. Chaque chemin calcule une fois Argon2 : le temps de réponse ne révèle
    // donc ni si l'adresse existe, ni si le compte est bloqué (SS §2).
    public async Task<AccountLoginResult> LogVisitorInWithEmailAddressAndPasswordAsync(LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        var passwordWrittenInItsNormalizedForm = PasswordRules.ConvertPasswordIntoItsNormalizedForm(loginAttemptReceivedFromTheVisitor.PasswordTextTypedByTheVisitor);
        var accountAlreadyUsingThisEmailAddress = await FindAccountIfEmailAddressIsWellFormedAsync(loginAttemptReceivedFromTheVisitor.EmailAddressTypedByTheVisitor);

        if (accountAlreadyUsingThisEmailAddress == null)
        {
            hasherOfThePasswordsTypedByVisitors.ConvertPasswordIntoItsSecureHash(passwordWrittenInItsNormalizedForm);
            return await RefuseLoginAttemptWithWrongCredentialsAsync(null, loginAttemptReceivedFromTheVisitor);
        }

        if (!accountAlreadyUsingThisEmailAddress.IsEmailConfirmed)
        {
            return await CheckPasswordOfTheAccountNotYetConfirmedAsync(accountAlreadyUsingThisEmailAddress, passwordWrittenInItsNormalizedForm, loginAttemptReceivedFromTheVisitor);
        }

        return await CheckPasswordThenLogConfirmedAccountInAsync(accountAlreadyUsingThisEmailAddress, passwordWrittenInItsNormalizedForm, loginAttemptReceivedFromTheVisitor);
    }

    // Cherche le compte. Une adresse mal formée n'est jamais envoyée à la base : aucun compte ne peut l'utiliser,
    // et certains caractères, comme le caractère nul, y provoqueraient une erreur.
    private async Task<User?> FindAccountIfEmailAddressIsWellFormedAsync(string emailAddressTypedByTheVisitor)
    {
        var emailAddressInItsStoredForm = EmailAddressRules.ConvertEmailAddressIntoItsStoredForm(emailAddressTypedByTheVisitor);

        if (!EmailAddressRules.CheckThatEmailAddressHasValidFormat(emailAddressInItsStoredForm))
        {
            return null;
        }

        return await repositoryOfTheUserAccountsInDatabase.FindUserAccountByEmailAddressAsync(emailAddressInItsStoredForm);
    }

    // Un compte non confirmé n'est jamais bloqué : ses essais ne comptent pas (SS §2).
    // Le message qui invite à confirmer n'apparaît qu'avec le bon mot de passe.
    private async Task<AccountLoginResult> CheckPasswordOfTheAccountNotYetConfirmedAsync(
        User accountWithEmailAddressNotYetConfirmed,
        string passwordWrittenInItsNormalizedForm,
        LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        var passwordIsTheOneOfTheAccount = hasherOfThePasswordsTypedByVisitors.CheckThatPasswordMatchesItsSecureHash(passwordWrittenInItsNormalizedForm, accountWithEmailAddressNotYetConfirmed.PasswordHash);

        if (!passwordIsTheOneOfTheAccount)
        {
            return await RefuseLoginAttemptWithWrongCredentialsAsync(accountWithEmailAddressNotYetConfirmed.Id, loginAttemptReceivedFromTheVisitor);
        }

        await SaveSecurityEventOfThisLoginAttemptAsync(SecurityEventType.Login, SecurityEventResult.Failure, accountWithEmailAddressNotYetConfirmed.Id, loginAttemptReceivedFromTheVisitor);
        return new AccountLoginResult { EmailAddressNotConfirmedErrorMessage = ErrorMessageForEmailAddressNotConfirmed };
    }

    // Compte l'essai, vérifie le mot de passe, puis ouvre la session si tout est bon.
    // Pendant un blocage, même le bon mot de passe reçoit le message générique (SS §2).
    private async Task<AccountLoginResult> CheckPasswordThenLogConfirmedAccountInAsync(
        User accountWithEmailAddressAlreadyConfirmed,
        string passwordWrittenInItsNormalizedForm,
        LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        var resultOfTheCountOfThisAttempt = await TryToCountThisLoginAttemptAsync(accountWithEmailAddressAlreadyConfirmed.Id);
        var passwordIsTheOneOfTheAccount = hasherOfThePasswordsTypedByVisitors.CheckThatPasswordMatchesItsSecureHash(passwordWrittenInItsNormalizedForm, accountWithEmailAddressAlreadyConfirmed.PasswordHash);

        if (resultOfTheCountOfThisAttempt == ResultOfTheCountOfOneLoginAttempt.AttemptRefused || !passwordIsTheOneOfTheAccount)
        {
            return await RefuseAttemptOnConfirmedAccountAsync(accountWithEmailAddressAlreadyConfirmed.Id, resultOfTheCountOfThisAttempt, loginAttemptReceivedFromTheVisitor);
        }

        return await OpenNewSessionForThisAccountAsync(accountWithEmailAddressAlreadyConfirmed.Id, loginAttemptReceivedFromTheVisitor);
    }

    // Compte l'essai en base. Si une autre requête a changé le compte entre-temps, relit le compte et recommence :
    // ainsi, aucun essai ne se perd, même avec des requêtes simultanées.
    private async Task<ResultOfTheCountOfOneLoginAttempt> TryToCountThisLoginAttemptAsync(Guid identifierOfTheAccountLoggingIn)
    {
        var dateOfThisLoginAttemptOfTheVisitor = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime;

        for (var numberOfTheTryBeingDone = 1; numberOfTheTryBeingDone <= MaximumNumberOfTriesToSaveTheLoginOfOneAccount; numberOfTheTryBeingDone++)
        {
            var accountReadBeforeCountingTheAttempt = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(identifierOfTheAccountLoggingIn);

            if (accountReadBeforeCountingTheAttempt == null
                || AccountLockoutRules.CheckThatAccountIsLockedAtThisDate(accountReadBeforeCountingTheAttempt, dateOfThisLoginAttemptOfTheVisitor))
            {
                return ResultOfTheCountOfOneLoginAttempt.AttemptRefused;
            }

            var thisLoginAttemptLockedTheAccount = AccountLockoutRules.CountOneLoginAttemptAndLockAccountOnTheLastOne(accountReadBeforeCountingTheAttempt, dateOfThisLoginAttemptOfTheVisitor);

            if (await repositoryOfTheUserAccountsInDatabase.TryToSaveLoginCountersOfThisAccountAsync(accountReadBeforeCountingTheAttempt))
            {
                return thisLoginAttemptLockedTheAccount
                    ? ResultOfTheCountOfOneLoginAttempt.AttemptCountedAndAccountLocked
                    : ResultOfTheCountOfOneLoginAttempt.AttemptCountedAndAccountStillOpen;
            }
        }

        return ResultOfTheCountOfOneLoginAttempt.AttemptRefused;
    }

    // Note le refus dans le journal, et le blocage si c'est cet essai qui l'a déclenché :
    // une seule requête peut l'avoir déclenché, le blocage n'est donc noté qu'une fois.
    private async Task<AccountLoginResult> RefuseAttemptOnConfirmedAccountAsync(
        Guid identifierOfTheAccountAlreadyConfirmed,
        ResultOfTheCountOfOneLoginAttempt resultOfTheCountOfThisAttempt,
        LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        if (resultOfTheCountOfThisAttempt == ResultOfTheCountOfOneLoginAttempt.AttemptCountedAndAccountLocked)
        {
            await SaveSecurityEventOfThisLoginAttemptAsync(SecurityEventType.Lockout, SecurityEventResult.Success, identifierOfTheAccountAlreadyConfirmed, loginAttemptReceivedFromTheVisitor);
        }

        return await RefuseLoginAttemptWithWrongCredentialsAsync(identifierOfTheAccountAlreadyConfirmed, loginAttemptReceivedFromTheVisitor);
    }

    // Ouvre une session avec un nouvel identifiant (SS §3), et enregistre l'appareil s'il est nouveau.
    private async Task<AccountLoginResult> OpenNewSessionForThisAccountAsync(Guid identifierOfTheAccountLoggingIn, LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        var generatedTokenOfTheNewSession = generatorOfTheSecureRandomTokens.GenerateNewRandomSecureTokenWithItsHash();
        var generatedTokenOfTheNewDevice = await GenerateDeviceTokenIfThisDeviceIsNewForTheAccountAsync(identifierOfTheAccountLoggingIn, loginAttemptReceivedFromTheVisitor.DeviceTokenReadInTheCookie);

        var loginWasSavedInTheDatabase = await TryToSaveLoginUnlessOtherRequestsKeepChangingTheAccountAsync(
            identifierOfTheAccountLoggingIn,
            CreateSessionFromThisGeneratedToken(generatedTokenOfTheNewSession, identifierOfTheAccountLoggingIn),
            CreateDeviceFromThisGeneratedToken(generatedTokenOfTheNewDevice, identifierOfTheAccountLoggingIn),
            CreateSecurityEventOfThisLoginAttempt(SecurityEventType.Login, SecurityEventResult.Success, identifierOfTheAccountLoggingIn, loginAttemptReceivedFromTheVisitor));

        if (!loginWasSavedInTheDatabase)
        {
            return await RefuseLoginAttemptWithWrongCredentialsAsync(identifierOfTheAccountLoggingIn, loginAttemptReceivedFromTheVisitor);
        }

        await CloseOldSessionOfThisBrowserAsync(loginAttemptReceivedFromTheVisitor.SessionTokenReadInTheCookie);
        return CreateResultForTheSuccessfulLogin(generatedTokenOfTheNewSession, generatedTokenOfTheNewDevice, loginAttemptReceivedFromTheVisitor.DeviceTokenReadInTheCookie);
    }

    // Enregistre la connexion. Si une autre requête a changé le compte pendant le hachage, relit le compte et recommence.
    // Renvoie faux si le compte a disparu, ou s'il change encore au bout de 10 passages.
    private async Task<bool> TryToSaveLoginUnlessOtherRequestsKeepChangingTheAccountAsync(
        Guid identifierOfTheAccountLoggingIn,
        UserSession newSessionOfTheUserAccount,
        Device? newDeviceOfTheUserAccount,
        SecurityEvent securityEventOfTheSuccessfulLogin)
    {
        for (var numberOfTheTryBeingDone = 1; numberOfTheTryBeingDone <= MaximumNumberOfTriesToSaveTheLoginOfOneAccount; numberOfTheTryBeingDone++)
        {
            var accountReadBeforeSavingTheLogin = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(identifierOfTheAccountLoggingIn);
            if (accountReadBeforeSavingTheLogin == null)
            {
                return false;
            }

            AccountLockoutRules.ResetLoginCountersAfterSuccessfulLogin(accountReadBeforeSavingTheLogin);

            if (await repositoryOfTheUserSessionsInDatabase.TryToSaveSuccessfulLoginOfThisAccountAsync(accountReadBeforeSavingTheLogin, newSessionOfTheUserAccount, newDeviceOfTheUserAccount, securityEventOfTheSuccessfulLogin))
            {
                return true;
            }
        }

        return false;
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

    // Donne les jetons à écrire dans les cookies : l'appareil garde son jeton s'il est déjà connu du compte.
    private static AccountLoginResult CreateResultForTheSuccessfulLogin(
        GeneratedSecureToken generatedTokenOfTheNewSession,
        GeneratedSecureToken? generatedTokenOfTheNewDevice,
        string? deviceTokenReadInTheCookie)
    {
        return new AccountLoginResult
        {
            SessionTokenToPutInTheCookie = generatedTokenOfTheNewSession.TokenGivenOnlyToTheVisitor,
            DeviceTokenToPutInTheCookie = generatedTokenOfTheNewDevice?.TokenGivenOnlyToTheVisitor ?? deviceTokenReadInTheCookie
        };
    }

    // Note l'échec dans le journal, et donne le même message dans tous les cas (GR Auth).
    private async Task<AccountLoginResult> RefuseLoginAttemptWithWrongCredentialsAsync(Guid? identifierOfTheAccountIfItExists, LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        await SaveSecurityEventOfThisLoginAttemptAsync(SecurityEventType.Login, SecurityEventResult.Failure, identifierOfTheAccountIfItExists, loginAttemptReceivedFromTheVisitor);

        return new AccountLoginResult { WrongCredentialsErrorMessage = ErrorMessageForWrongLoginCredentials };
    }

    // Écrit un événement dans le journal de sécurité (SS §11).
    private async Task SaveSecurityEventOfThisLoginAttemptAsync(
        SecurityEventType typeOfTheSecurityEventToSave,
        SecurityEventResult resultOfTheSecurityEventToSave,
        Guid? identifierOfTheAccountIfItExists,
        LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        var securityEventToSaveInTheJournal = CreateSecurityEventOfThisLoginAttempt(typeOfTheSecurityEventToSave, resultOfTheSecurityEventToSave, identifierOfTheAccountIfItExists, loginAttemptReceivedFromTheVisitor);

        await repositoryOfTheSecurityEventsInDatabase.SaveSecurityEventInTheJournalAsync(securityEventToSaveInTheJournal);
    }

    // Crée un événement du journal : date, utilisateur, adresse IP, événement et résultat. Jamais l'adresse e-mail (SS §7 et §11).
    private SecurityEvent CreateSecurityEventOfThisLoginAttempt(
        SecurityEventType typeOfTheSecurityEventToSave,
        SecurityEventResult resultOfTheSecurityEventToSave,
        Guid? identifierOfTheAccountIfItExists,
        LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        return new SecurityEvent
        {
            Type = typeOfTheSecurityEventToSave,
            Result = resultOfTheSecurityEventToSave,
            OccurredAt = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime,
            IpAddress = loginAttemptReceivedFromTheVisitor.IpAddressOfTheVisitor,
            UserId = identifierOfTheAccountIfItExists
        };
    }
}
