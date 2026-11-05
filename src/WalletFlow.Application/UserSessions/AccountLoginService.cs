using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Cas d'utilisation : un visiteur se connecte avec son adresse e-mail et son mot de passe (GR Auth, SS §2 et §3).
// Si la double authentification est activée, le bon mot de passe ne suffit pas : la connexion attend alors son code.
public class AccountLoginService
{
    public const string ErrorMessageForWrongLoginCredentials = "Identifiants incorrects";

    public const string ErrorMessageForEmailAddressNotConfirmed =
        "Ton adresse e-mail n'est pas encore confirmée. Ouvre le lien reçu par e-mail, ou inscris-toi à nouveau pour en recevoir un autre.";

    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase;
    private readonly IBackgroundEmailRequestQueue queueOfTheEmailsToPrepareInTheBackground;
    private readonly IPasswordHasher hasherOfThePasswordsTypedByVisitors;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomTokens;
    private readonly ILoginsWaitingForTheirCodeStore storeOfTheLoginsWaitingForTheirCode;
    private readonly UserSessionOpeningService serviceThatOpensTheUserSessions;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont la connexion a besoin.
    public AccountLoginService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase,
        IBackgroundEmailRequestQueue queueOfTheEmailsToPrepareInTheBackground,
        IPasswordHasher hasherOfThePasswordsTypedByVisitors,
        ISecureTokenGenerator generatorOfTheSecureRandomTokens,
        ILoginsWaitingForTheirCodeStore storeOfTheLoginsWaitingForTheirCode,
        UserSessionOpeningService serviceThatOpensTheUserSessions,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.repositoryOfTheSecurityEventsInDatabase = repositoryOfTheSecurityEventsInDatabase;
        this.queueOfTheEmailsToPrepareInTheBackground = queueOfTheEmailsToPrepareInTheBackground;
        this.hasherOfThePasswordsTypedByVisitors = hasherOfThePasswordsTypedByVisitors;
        this.generatorOfTheSecureRandomTokens = generatorOfTheSecureRandomTokens;
        this.storeOfTheLoginsWaitingForTheirCode = storeOfTheLoginsWaitingForTheirCode;
        this.serviceThatOpensTheUserSessions = serviceThatOpensTheUserSessions;
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

    // Compte l'essai, vérifie le mot de passe, puis ouvre la session si tout est bon, ou attend le code.
    // Pendant un blocage, même le bon mot de passe reçoit le message générique (SS §2).
    private async Task<AccountLoginResult> CheckPasswordThenLogConfirmedAccountInAsync(
        User accountWithEmailAddressAlreadyConfirmed,
        string passwordWrittenInItsNormalizedForm,
        LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        var secureHashOfThePasswordJustChecked = accountWithEmailAddressAlreadyConfirmed.PasswordHash;
        var encryptedTotpSecretOfTheAccount = accountWithEmailAddressAlreadyConfirmed.EncryptedTotpSecret;
        var resultOfTheCountOfThisAttempt = await TryToCountThisLoginAttemptAsync(accountWithEmailAddressAlreadyConfirmed.Id);
        var passwordIsTheOneOfTheAccount = hasherOfThePasswordsTypedByVisitors.CheckThatPasswordMatchesItsSecureHash(passwordWrittenInItsNormalizedForm, secureHashOfThePasswordJustChecked);

        if (resultOfTheCountOfThisAttempt == ResultOfTheCountOfOneLoginAttempt.AttemptRefused || !passwordIsTheOneOfTheAccount)
        {
            return await RefuseAttemptOnConfirmedAccountAsync(accountWithEmailAddressAlreadyConfirmed.Id, resultOfTheCountOfThisAttempt, loginAttemptReceivedFromTheVisitor);
        }

        if (encryptedTotpSecretOfTheAccount != null)
        {
            return await StartWaitingForTheCodeOfThisAccountAsync(accountWithEmailAddressAlreadyConfirmed.Id, secureHashOfThePasswordJustChecked, resultOfTheCountOfThisAttempt, loginAttemptReceivedFromTheVisitor);
        }

        return await OpenNewSessionForThisAccountAsync(accountWithEmailAddressAlreadyConfirmed.Id, secureHashOfThePasswordJustChecked, loginAttemptReceivedFromTheVisitor);
    }

    // Compte l'essai en base. Si une autre requête a changé le compte entre-temps, relit le compte et recommence :
    // ainsi, aucun essai ne se perd, même avec des requêtes simultanées.
    private async Task<ResultOfTheCountOfOneLoginAttempt> TryToCountThisLoginAttemptAsync(Guid identifierOfTheAccountLoggingIn)
    {
        var dateOfThisLoginAttemptOfTheVisitor = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime;

        for (var numberOfTheTryBeingDone = 1; numberOfTheTryBeingDone <= AccountLockoutRules.MaximumNumberOfTriesWhileOtherRequestsChangeTheAccount; numberOfTheTryBeingDone++)
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

    // Note le refus dans le journal, avec le blocage si c'est cet essai qui l'a déclenché.
    private async Task<AccountLoginResult> RefuseAttemptOnConfirmedAccountAsync(
        Guid identifierOfTheAccountAlreadyConfirmed,
        ResultOfTheCountOfOneLoginAttempt resultOfTheCountOfThisAttempt,
        LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        await NotifyOwnerIfThisAttemptLockedTheAccountAsync(identifierOfTheAccountAlreadyConfirmed, resultOfTheCountOfThisAttempt, loginAttemptReceivedFromTheVisitor);

        return await RefuseLoginAttemptWithWrongCredentialsAsync(identifierOfTheAccountAlreadyConfirmed, loginAttemptReceivedFromTheVisitor);
    }

    // Une seule requête peut avoir déclenché le blocage : il n'est donc noté qu'une fois, et un seul e-mail de déblocage part (SS §2).
    // Cet e-mail part en arrière-plan : le temps de réponse ne révèle donc pas que le compte existe.
    private async Task NotifyOwnerIfThisAttemptLockedTheAccountAsync(
        Guid identifierOfTheAccountAlreadyConfirmed,
        ResultOfTheCountOfOneLoginAttempt resultOfTheCountOfThisAttempt,
        LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        if (resultOfTheCountOfThisAttempt != ResultOfTheCountOfOneLoginAttempt.AttemptCountedAndAccountLocked)
        {
            return;
        }

        await SaveSecurityEventOfThisLoginAttemptAsync(SecurityEventType.Lockout, SecurityEventResult.Success, identifierOfTheAccountAlreadyConfirmed, loginAttemptReceivedFromTheVisitor);

        queueOfTheEmailsToPrepareInTheBackground.AddEmailRequestToTheBackgroundQueue(new BackgroundEmailRequest
        {
            TypeOfTheRequestedBackgroundEmail = BackgroundEmailRequestType.AccountUnlock,
            IdentifierOfTheAccountJustLocked = identifierOfTheAccountAlreadyConfirmed,
            IpAddressOfTheRequestingVisitor = loginAttemptReceivedFromTheVisitor.IpAddressOfTheVisitor
        });
    }

    // Le mot de passe est bon, mais le compte demande aussi un code : aucune session ne s'ouvre, et l'essai reste compté.
    // Seul le bon code remet les compteurs à zéro : un attaquant qui a volé le mot de passe finit donc bloqué, et le propriétaire est prévenu (GR Auth).
    // Si cet essai vient de bloquer le compte, le code reste permis : le vrai propriétaire retrouve l'accès, et ce blocage disparaît.
    private async Task<AccountLoginResult> StartWaitingForTheCodeOfThisAccountAsync(
        Guid identifierOfTheAccountLoggingIn,
        string secureHashOfThePasswordJustChecked,
        ResultOfTheCountOfOneLoginAttempt resultOfTheCountOfThisAttempt,
        LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        await NotifyOwnerIfThisAttemptLockedTheAccountAsync(identifierOfTheAccountLoggingIn, resultOfTheCountOfThisAttempt, loginAttemptReceivedFromTheVisitor);

        var generatedTokenOfTheWaitingLogin = generatorOfTheSecureRandomTokens.GenerateNewRandomSecureTokenWithItsHash();
        storeOfTheLoginsWaitingForTheirCode.SaveNewLoginWaitingForItsCode(generatedTokenOfTheWaitingLogin.HashToStoreInTheDatabase, identifierOfTheAccountLoggingIn, secureHashOfThePasswordJustChecked);

        return new AccountLoginResult { WaitingLoginTokenToPutInTheCookie = generatedTokenOfTheWaitingLogin.TokenGivenOnlyToTheVisitor };
    }

    // Ouvre la session d'un compte sans double authentification. Si elle vient d'être activée, la connexion est refusée.
    private async Task<AccountLoginResult> OpenNewSessionForThisAccountAsync(
        Guid identifierOfTheAccountLoggingIn,
        string secureHashOfThePasswordJustChecked,
        LoginAttemptFromVisitor loginAttemptReceivedFromTheVisitor)
    {
        var sessionJustOpenedForTheAccount = await serviceThatOpensTheUserSessions.TryToOpenSessionForThisSuccessfulLoginAsync(new SuccessfulLoginToSave
        {
            IdentifierOfTheAccountLoggingIn = identifierOfTheAccountLoggingIn,
            SecureHashOfThePasswordJustChecked = secureHashOfThePasswordJustChecked,
            IpAddressOfTheVisitor = loginAttemptReceivedFromTheVisitor.IpAddressOfTheVisitor,
            DeviceTokenReadInTheCookie = loginAttemptReceivedFromTheVisitor.DeviceTokenReadInTheCookie,
            SessionTokenReadInTheCookie = loginAttemptReceivedFromTheVisitor.SessionTokenReadInTheCookie
        });

        if (sessionJustOpenedForTheAccount == null)
        {
            return await RefuseLoginAttemptWithWrongCredentialsAsync(identifierOfTheAccountLoggingIn, loginAttemptReceivedFromTheVisitor);
        }

        return new AccountLoginResult
        {
            SessionTokenToPutInTheCookie = sessionJustOpenedForTheAccount.SessionTokenToPutInTheCookie,
            DeviceTokenToPutInTheCookie = sessionJustOpenedForTheAccount.DeviceTokenToPutInTheCookie
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
