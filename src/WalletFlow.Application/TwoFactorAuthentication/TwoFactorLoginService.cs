using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Cas d'utilisation : un visiteur qui a donné le bon mot de passe finit sa connexion avec un code (GR Auth, SS §2 et §6).
public class TwoFactorLoginService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly SecurityJournalService securityJournal;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly IWaitingLoginStore waitingLoginStore;
    private readonly TwoFactorCodeCheckingService codeCheckingService;
    private readonly UserSessionOpeningService sessionOpeningService;

    // Reçoit les outils dont la fin de la connexion a besoin.
    public TwoFactorLoginService(
        IUserAccountRepository accountRepository,
        SecurityJournalService securityJournal,
        ISecureTokenGenerator tokenGenerator,
        IWaitingLoginStore waitingLoginStore,
        TwoFactorCodeCheckingService codeCheckingService,
        UserSessionOpeningService sessionOpeningService)
    {
        this.accountRepository = accountRepository;
        this.securityJournal = securityJournal;
        this.tokenGenerator = tokenGenerator;
        this.waitingLoginStore = waitingLoginStore;
        this.codeCheckingService = codeCheckingService;
        this.sessionOpeningService = sessionOpeningService;
    }

    // Vérifie le code de la connexion en attente, puis ouvre la session. Seul ce moment rend la connexion réussie (GR Auth).
    public async Task<TwoFactorLoginResult> FinishAsync(TwoFactorCodeAttempt attempt)
    {
        if (attempt.WaitingLoginToken == null)
        {
            return new TwoFactorLoginResult.LoginMustRestart();
        }

        var tokenHash = tokenGenerator.HashToken(attempt.WaitingLoginToken);
        var waitingLogin = waitingLoginStore.TryCountCodeAttempt(tokenHash);

        if (waitingLogin == null)
        {
            return new TwoFactorLoginResult.LoginMustRestart();
        }

        var account = await accountRepository.FindByIdAsync(waitingLogin.AccountId);

        if (account == null || !IsStillValid(waitingLogin, account))
        {
            return await AbandonAsync(tokenHash, account?.Id, attempt);
        }

        // Lu avant la vérification, pas après : l'enregistrement contrôlera que ce secret, celui qui a servi au contrôle, est toujours le bon.
        var encryptedTotpSecret = account.EncryptedTotpSecret;
        var codeCheck = await codeCheckingService.CheckAsync(account, attempt.Code);

        if (!codeCheck.IsCodeValid)
        {
            return await RefuseWrongCodeAsync(tokenHash, waitingLogin, attempt);
        }

        return await OpenSessionAsync(tokenHash, waitingLogin, encryptedTotpSecret, codeCheck, attempt);
    }

    // Une récupération du mot de passe ou une désactivation a pu passer pendant l'attente.
    private static bool IsStillValid(WaitingLogin waitingLogin, User account)
    {
        return account.IsTwoFactorEnabled
            && account.PasswordHash == waitingLogin.PasswordHash;
    }

    // Note l'échec dans le journal. Au 5e mauvais code, la connexion doit recommencer, ce qui compte un essai de plus sur le compte (SS §6).
    private async Task<TwoFactorLoginResult> RefuseWrongCodeAsync(
        string tokenHash,
        WaitingLogin waitingLogin,
        TwoFactorCodeAttempt attempt)
    {
        if (waitingLogin.TriedCodeCount >= TwoFactorAuthenticationRules.MaximumCodesPerLogin)
        {
            return await AbandonAsync(tokenHash, waitingLogin.AccountId, attempt);
        }

        await securityJournal.SaveEventNowAsync(SecurityEventType.Login, SecurityEventResult.Failure, waitingLogin.AccountId, attempt.IpAddress);

        return new TwoFactorLoginResult.WrongCode();
    }

    // Ouvre la session, marque le code de secours comme utilisé s'il y en a un, puis oublie la connexion en attente.
    private async Task<TwoFactorLoginResult> OpenSessionAsync(
        string tokenHash,
        WaitingLogin waitingLogin,
        string? encryptedTotpSecret,
        TwoFactorCodeCheckResult codeCheck,
        TwoFactorCodeAttempt attempt)
    {
        var session = await sessionOpeningService.TryOpenSessionAsync(new SuccessfulLogin
        {
            AccountId = waitingLogin.AccountId,
            PasswordHash = waitingLogin.PasswordHash,
            EncryptedTotpSecret = encryptedTotpSecret,
            BackupCodeId = codeCheck.BackupCodeId,
            IpAddress = attempt.IpAddress,
            DeviceToken = attempt.DeviceToken,
            SessionToken = attempt.SessionToken
        });

        if (session == null)
        {
            return await AbandonAsync(tokenHash, waitingLogin.AccountId, attempt);
        }

        waitingLoginStore.Remove(tokenHash);

        return new TwoFactorLoginResult.LoggedIn
        {
            SessionToken = session.SessionToken,
            DeviceToken = session.DeviceToken
        };
    }

    // Oublie la connexion en attente, note l'échec dans le journal, et demande de recommencer.
    private async Task<TwoFactorLoginResult> AbandonAsync(string tokenHash, Guid? accountId, TwoFactorCodeAttempt attempt)
    {
        waitingLoginStore.Remove(tokenHash);

        await securityJournal.SaveEventNowAsync(SecurityEventType.Login, SecurityEventResult.Failure, accountId, attempt.IpAddress);

        return new TwoFactorLoginResult.LoginMustRestart();
    }
}
