using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Cas d'utilisation : un visiteur qui a donné le bon mot de passe finit sa connexion avec un code (GR Auth, SS §2 et §6).
public class TwoFactorLoginService
{
    public const string ErrorMessageForLoginThatMustRestart = "Cette connexion n'est plus valable. Reconnecte-toi.";

    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomTokens;
    private readonly ILoginsWaitingForTheirCodeStore storeOfTheLoginsWaitingForTheirCode;
    private readonly TwoFactorCodeCheckingService serviceThatChecksTheTwoFactorCodes;
    private readonly UserSessionOpeningService serviceThatOpensTheUserSessions;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont la fin de la connexion a besoin.
    public TwoFactorLoginService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase,
        ISecureTokenGenerator generatorOfTheSecureRandomTokens,
        ILoginsWaitingForTheirCodeStore storeOfTheLoginsWaitingForTheirCode,
        TwoFactorCodeCheckingService serviceThatChecksTheTwoFactorCodes,
        UserSessionOpeningService serviceThatOpensTheUserSessions,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.repositoryOfTheSecurityEventsInDatabase = repositoryOfTheSecurityEventsInDatabase;
        this.generatorOfTheSecureRandomTokens = generatorOfTheSecureRandomTokens;
        this.storeOfTheLoginsWaitingForTheirCode = storeOfTheLoginsWaitingForTheirCode;
        this.serviceThatChecksTheTwoFactorCodes = serviceThatChecksTheTwoFactorCodes;
        this.serviceThatOpensTheUserSessions = serviceThatOpensTheUserSessions;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Vérifie le code de la connexion en attente, puis ouvre la session. Seul ce moment rend la connexion réussie (GR Auth).
    public async Task<TwoFactorLoginResult> FinishLoginOfTheVisitorWithThisCodeAsync(TwoFactorCodeAttemptFromVisitor codeAttemptReceivedFromTheVisitor)
    {
        if (codeAttemptReceivedFromTheVisitor.WaitingLoginTokenReadInTheCookie == null)
        {
            return CreateResultForTheLoginThatMustRestart();
        }

        var hashOfTheTokenOfTheWaitingLogin = generatorOfTheSecureRandomTokens.ConvertSecureTokenIntoItsHash(codeAttemptReceivedFromTheVisitor.WaitingLoginTokenReadInTheCookie);
        var loginOfTheVisitorWaitingForItsCode = storeOfTheLoginsWaitingForTheirCode.TryToCountOneCodeAttemptOfThisWaitingLogin(hashOfTheTokenOfTheWaitingLogin);

        if (loginOfTheVisitorWaitingForItsCode == null)
        {
            return CreateResultForTheLoginThatMustRestart();
        }

        var accountOwningTheLoginWaitingForItsCode = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(loginOfTheVisitorWaitingForItsCode.IdentifierOfTheAccountLoggingIn);

        if (!CheckThatWaitingLoginIsStillValidForThisAccount(loginOfTheVisitorWaitingForItsCode, accountOwningTheLoginWaitingForItsCode))
        {
            return await AbandonWaitingLoginOfTheVisitorAsync(hashOfTheTokenOfTheWaitingLogin, accountOwningTheLoginWaitingForItsCode?.Id, codeAttemptReceivedFromTheVisitor);
        }

        // Lu avant la vérification, pas après : l'enregistrement contrôlera que ce secret, celui qui a servi au contrôle, est toujours le bon.
        var encryptedTotpSecretOfTheCodeJustChecked = accountOwningTheLoginWaitingForItsCode!.EncryptedTotpSecret;
        var resultOfTheCheckOfTheCode = await serviceThatChecksTheTwoFactorCodes.CheckThatCodeIsValidForThisActivatedAccountAsync(accountOwningTheLoginWaitingForItsCode, codeAttemptReceivedFromTheVisitor.CodeTypedByTheVisitor);

        if (!resultOfTheCheckOfTheCode.IsCodeValid)
        {
            return await RefuseWrongCodeOfTheVisitorAsync(hashOfTheTokenOfTheWaitingLogin, loginOfTheVisitorWaitingForItsCode, codeAttemptReceivedFromTheVisitor);
        }

        return await OpenSessionOfTheWaitingLoginAsync(hashOfTheTokenOfTheWaitingLogin, loginOfTheVisitorWaitingForItsCode, encryptedTotpSecretOfTheCodeJustChecked, resultOfTheCheckOfTheCode, codeAttemptReceivedFromTheVisitor);
    }

    // Une récupération du mot de passe, une désactivation ou un effacement a pu passer pendant l'attente.
    private static bool CheckThatWaitingLoginIsStillValidForThisAccount(LoginWaitingForItsCode loginOfTheVisitorWaitingForItsCode, User? accountOwningTheLoginWaitingForItsCode)
    {
        return accountOwningTheLoginWaitingForItsCode != null
            && accountOwningTheLoginWaitingForItsCode.IsTwoFactorEnabled
            && accountOwningTheLoginWaitingForItsCode.PasswordHash == loginOfTheVisitorWaitingForItsCode.SecureHashOfThePasswordJustChecked;
    }

    // Note l'échec dans le journal. Au 5e mauvais code, la connexion doit recommencer, ce qui compte un essai de plus sur le compte (SS §6).
    private async Task<TwoFactorLoginResult> RefuseWrongCodeOfTheVisitorAsync(
        string hashOfTheTokenOfTheWaitingLogin,
        LoginWaitingForItsCode loginOfTheVisitorWaitingForItsCode,
        TwoFactorCodeAttemptFromVisitor codeAttemptReceivedFromTheVisitor)
    {
        if (loginOfTheVisitorWaitingForItsCode.NumberOfCodesTriedSoFar >= TwoFactorAuthenticationRules.MaximumNumberOfCodesTriedDuringOneLogin)
        {
            return await AbandonWaitingLoginOfTheVisitorAsync(hashOfTheTokenOfTheWaitingLogin, loginOfTheVisitorWaitingForItsCode.IdentifierOfTheAccountLoggingIn, codeAttemptReceivedFromTheVisitor);
        }

        await SaveFailedLoginInTheSecurityJournalAsync(loginOfTheVisitorWaitingForItsCode.IdentifierOfTheAccountLoggingIn, codeAttemptReceivedFromTheVisitor);

        return new TwoFactorLoginResult { WrongCodeErrorMessage = TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode };
    }

    // Ouvre la session, marque le code de secours comme utilisé s'il y en a un, puis oublie la connexion en attente.
    private async Task<TwoFactorLoginResult> OpenSessionOfTheWaitingLoginAsync(
        string hashOfTheTokenOfTheWaitingLogin,
        LoginWaitingForItsCode loginOfTheVisitorWaitingForItsCode,
        string? encryptedTotpSecretOfTheCodeJustChecked,
        TwoFactorCodeCheckResult resultOfTheCheckOfTheCode,
        TwoFactorCodeAttemptFromVisitor codeAttemptReceivedFromTheVisitor)
    {
        var sessionJustOpenedForTheAccount = await serviceThatOpensTheUserSessions.TryToOpenSessionForThisSuccessfulLoginAsync(new SuccessfulLoginToSave
        {
            IdentifierOfTheAccountLoggingIn = loginOfTheVisitorWaitingForItsCode.IdentifierOfTheAccountLoggingIn,
            SecureHashOfThePasswordJustChecked = loginOfTheVisitorWaitingForItsCode.SecureHashOfThePasswordJustChecked,
            EncryptedTotpSecretJustChecked = encryptedTotpSecretOfTheCodeJustChecked,
            IdentifierOfTheBackupCodeJustUsed = resultOfTheCheckOfTheCode.IdentifierOfTheBackupCodeUsed,
            IpAddressOfTheVisitor = codeAttemptReceivedFromTheVisitor.IpAddressOfTheVisitor,
            DeviceTokenReadInTheCookie = codeAttemptReceivedFromTheVisitor.DeviceTokenReadInTheCookie,
            SessionTokenReadInTheCookie = codeAttemptReceivedFromTheVisitor.SessionTokenReadInTheCookie
        });

        if (sessionJustOpenedForTheAccount == null)
        {
            return await AbandonWaitingLoginOfTheVisitorAsync(hashOfTheTokenOfTheWaitingLogin, loginOfTheVisitorWaitingForItsCode.IdentifierOfTheAccountLoggingIn, codeAttemptReceivedFromTheVisitor);
        }

        storeOfTheLoginsWaitingForTheirCode.RemoveThisLoginWaitingForItsCode(hashOfTheTokenOfTheWaitingLogin);

        return new TwoFactorLoginResult
        {
            SessionTokenToPutInTheCookie = sessionJustOpenedForTheAccount.SessionTokenToPutInTheCookie,
            DeviceTokenToPutInTheCookie = sessionJustOpenedForTheAccount.DeviceTokenToPutInTheCookie
        };
    }

    // Oublie la connexion en attente, note l'échec dans le journal, et demande de recommencer.
    private async Task<TwoFactorLoginResult> AbandonWaitingLoginOfTheVisitorAsync(string hashOfTheTokenOfTheWaitingLogin, Guid? identifierOfTheAccountIfItExists, TwoFactorCodeAttemptFromVisitor codeAttemptReceivedFromTheVisitor)
    {
        storeOfTheLoginsWaitingForTheirCode.RemoveThisLoginWaitingForItsCode(hashOfTheTokenOfTheWaitingLogin);

        await SaveFailedLoginInTheSecurityJournalAsync(identifierOfTheAccountIfItExists, codeAttemptReceivedFromTheVisitor);

        return CreateResultForTheLoginThatMustRestart();
    }

    // Écrit l'échec dans le journal de sécurité : date, utilisateur, adresse IP, événement et résultat (SS §11).
    private async Task SaveFailedLoginInTheSecurityJournalAsync(Guid? identifierOfTheAccountIfItExists, TwoFactorCodeAttemptFromVisitor codeAttemptReceivedFromTheVisitor)
    {
        await repositoryOfTheSecurityEventsInDatabase.SaveSecurityEventInTheJournalAsync(new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Failure,
            OccurredAt = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime,
            IpAddress = codeAttemptReceivedFromTheVisitor.IpAddressOfTheVisitor,
            UserId = identifierOfTheAccountIfItExists
        });
    }

    // Donne le même refus dans tous les cas, sans dire pourquoi : attente finie, code de trop ou compte qui a changé.
    private static TwoFactorLoginResult CreateResultForTheLoginThatMustRestart()
    {
        return new TwoFactorLoginResult { LoginMustRestartErrorMessage = ErrorMessageForLoginThatMustRestart };
    }
}
