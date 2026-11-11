using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Cas d'utilisation : un visiteur se connecte avec son adresse e-mail et son mot de passe (GR Auth, SS §2 et §3).
// Si la double authentification est activée, le bon mot de passe ne suffit pas : la connexion attend alors son code.
public class AccountLoginService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly IBackgroundEmailRequestQueue emailQueue;
    private readonly IPasswordHasher passwordHasher;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly IWaitingLoginStore waitingLoginStore;
    private readonly UserSessionOpeningService sessionOpeningService;
    private readonly TimeProvider clock;

    // Reçoit les outils dont la connexion a besoin.
    public AccountLoginService(
        IUserAccountRepository accountRepository,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        IBackgroundEmailRequestQueue emailQueue,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        IWaitingLoginStore waitingLoginStore,
        UserSessionOpeningService sessionOpeningService,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.emailQueue = emailQueue;
        this.passwordHasher = passwordHasher;
        this.tokenGenerator = tokenGenerator;
        this.waitingLoginStore = waitingLoginStore;
        this.sessionOpeningService = sessionOpeningService;
        this.clock = clock;
    }

    // Connecte un visiteur. Chaque chemin calcule une fois Argon2 : le temps de réponse ne révèle
    // donc ni si l'adresse existe, ni si le compte est bloqué (SS §2). Le travail en base, lui, change selon le compte :
    // l'API donne donc la même durée minimale à chaque réponse (voir MinimumAnswerDurationFilter).
    public async Task<AccountLoginResult> LogInAsync(LoginAttempt attempt)
    {
        var normalizedPassword = PasswordRules.Normalize(attempt.Password);
        var account = await FindAccountAsync(attempt.EmailAddress);

        if (account == null)
        {
            await passwordHasher.HashAsync(normalizedPassword);
            return await RefuseAsync(null, attempt);
        }

        if (!account.IsEmailConfirmed)
        {
            return await CheckUnconfirmedAccountAsync(account, normalizedPassword, attempt);
        }

        return await LogInConfirmedAccountAsync(account, normalizedPassword, attempt);
    }

    // Cherche le compte. Une adresse mal formée n'est jamais envoyée à la base : aucun compte ne peut l'utiliser,
    // et certains caractères, comme le caractère nul, y provoqueraient une erreur.
    private async Task<User?> FindAccountAsync(string typedEmail)
    {
        var email = EmailAddressRules.Normalize(typedEmail);

        if (!EmailAddressRules.IsValid(email))
        {
            return null;
        }

        return await accountRepository.FindByEmailAsync(email);
    }

    // Un compte non confirmé n'est jamais bloqué : ses essais ne comptent pas (SS §2).
    // Le résultat qui invite à confirmer n'apparaît qu'avec le bon mot de passe.
    private async Task<AccountLoginResult> CheckUnconfirmedAccountAsync(
        User account,
        string normalizedPassword,
        LoginAttempt attempt)
    {
        var passwordMatches = await passwordHasher.VerifyAsync(normalizedPassword, account.PasswordHash);

        if (!passwordMatches)
        {
            return await RefuseAsync(account.Id, attempt);
        }

        await securityJournal.SaveEventNowAsync(SecurityEventType.Login, SecurityEventResult.Failure, account.Id, attempt.IpAddress);
        return new AccountLoginResult.EmailAddressNotConfirmed();
    }

    // Compte l'essai, vérifie le mot de passe, puis ouvre la session si tout est bon, ou attend le code.
    // Pendant un blocage, même le bon mot de passe reçoit le refus générique (SS §2).
    private async Task<AccountLoginResult> LogInConfirmedAccountAsync(
        User account,
        string normalizedPassword,
        LoginAttempt attempt)
    {
        var passwordHash = account.PasswordHash;
        var encryptedSecret = account.EncryptedTotpSecret;
        var countResult = await CountAttemptAsync(account.Id);
        var passwordMatches = await passwordHasher.VerifyAsync(normalizedPassword, passwordHash);

        if (countResult == LoginAttemptCountResult.Refused || !passwordMatches)
        {
            return await RefuseConfirmedAccountAsync(account.Id, countResult, attempt);
        }

        if (encryptedSecret != null)
        {
            return await StartWaitingForCodeAsync(account.Id, passwordHash, countResult, attempt);
        }

        return await OpenSessionAsync(account.Id, passwordHash, attempt);
    }

    // Compte l'essai en base. Si une autre requête a changé le compte entre-temps, le compte est relu et l'essai recompté :
    // ainsi, aucun essai ne se perd, même avec des requêtes simultanées.
    private async Task<LoginAttemptCountResult> CountAttemptAsync(Guid accountId)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        return await savingService.SaveWithRetriesAsync(
            () => PrepareAttemptCountAsync(accountId, now),
            LoginAttemptCountResult.Refused);
    }

    // Un passage : lit le compte, refuse s'il a disparu ou s'il est bloqué, sinon compte un essai de plus.
    private async Task<PreparedChanges<LoginAttemptCountResult>> PrepareAttemptCountAsync(Guid accountId, DateTime now)
    {
        var account = await accountRepository.FindByIdAsync(accountId);

        if (account == null
            || account.IsLockedAt(now))
        {
            return PreparedChanges<LoginAttemptCountResult>.NothingToSave(LoginAttemptCountResult.Refused);
        }

        if (account.CountLoginAttempt(now))
        {
            return PreparedChanges<LoginAttemptCountResult>.SaveThenReturn(LoginAttemptCountResult.CountedAndLocked);
        }

        return PreparedChanges<LoginAttemptCountResult>.SaveThenReturn(LoginAttemptCountResult.Counted);
    }

    // Note le refus dans le journal, avec le blocage si c'est cet essai qui l'a déclenché.
    private async Task<AccountLoginResult> RefuseConfirmedAccountAsync(
        Guid accountId,
        LoginAttemptCountResult countResult,
        LoginAttempt attempt)
    {
        await NotifyOwnerIfLockedAsync(accountId, countResult, attempt);

        return await RefuseAsync(accountId, attempt);
    }

    // Une seule requête peut avoir déclenché le blocage : il n'est donc noté qu'une fois, et un seul e-mail de déblocage part (SS §2).
    // Cet e-mail part en arrière-plan : le temps de réponse ne révèle donc pas que le compte existe.
    private async Task NotifyOwnerIfLockedAsync(
        Guid accountId,
        LoginAttemptCountResult countResult,
        LoginAttempt attempt)
    {
        if (countResult != LoginAttemptCountResult.CountedAndLocked)
        {
            return;
        }

        await securityJournal.SaveEventNowAsync(SecurityEventType.Lockout, SecurityEventResult.Success, accountId, attempt.IpAddress);

        emailQueue.Add(new BackgroundEmailRequest.AccountUnlock
        {
            AccountId = accountId,
            IpAddress = attempt.IpAddress
        });
    }

    // Le mot de passe est bon, mais le compte demande aussi un code : aucune session ne s'ouvre, et l'essai reste compté.
    // Seul le bon code remet les compteurs à zéro : un attaquant qui a volé le mot de passe finit donc bloqué, et le propriétaire est prévenu (GR Auth).
    // Si cet essai vient de bloquer le compte, le code reste permis : le vrai propriétaire retrouve l'accès, et ce blocage disparaît.
    private async Task<AccountLoginResult> StartWaitingForCodeAsync(
        Guid accountId,
        string passwordHash,
        LoginAttemptCountResult countResult,
        LoginAttempt attempt)
    {
        await NotifyOwnerIfLockedAsync(accountId, countResult, attempt);

        var waitingLoginToken = tokenGenerator.GenerateToken();
        waitingLoginStore.Save(waitingLoginToken.Hash, accountId, passwordHash);

        return new AccountLoginResult.WaitingForTwoFactorCode { WaitingLoginToken = waitingLoginToken.Token };
    }

    // Ouvre la session d'un compte sans double authentification. Si elle vient d'être activée, la connexion est refusée.
    private async Task<AccountLoginResult> OpenSessionAsync(
        Guid accountId,
        string passwordHash,
        LoginAttempt attempt)
    {
        var session = await sessionOpeningService.TryOpenSessionAsync(new SuccessfulLogin
        {
            AccountId = accountId,
            PasswordHash = passwordHash,
            IpAddress = attempt.IpAddress,
            DeviceToken = attempt.DeviceToken,
            SessionToken = attempt.SessionToken
        });

        if (session == null)
        {
            return await RefuseAsync(accountId, attempt);
        }

        return new AccountLoginResult.LoggedIn
        {
            SessionToken = session.SessionToken,
            DeviceToken = session.DeviceToken
        };
    }

    // Note l'échec dans le journal, et donne le même refus dans tous les cas (GR Auth).
    private async Task<AccountLoginResult> RefuseAsync(Guid? accountId, LoginAttempt attempt)
    {
        await securityJournal.SaveEventNowAsync(SecurityEventType.Login, SecurityEventResult.Failure, accountId, attempt.IpAddress);

        return new AccountLoginResult.WrongCredentials();
    }
}
