using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Cas d'utilisation : un visiteur se connecte avec son adresse e-mail et son mot de passe (GR Auth, SS §2 et §3).
// Si la double authentification est activée, le bon mot de passe ne suffit pas : la connexion attend alors son code.
public class AccountLoginService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly IEmailRequestRepository emailRequests;
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
        IEmailRequestRepository emailRequests,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        IWaitingLoginStore waitingLoginStore,
        UserSessionOpeningService sessionOpeningService,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.emailRequests = emailRequests;
        this.passwordHasher = passwordHasher;
        this.tokenGenerator = tokenGenerator;
        this.waitingLoginStore = waitingLoginStore;
        this.sessionOpeningService = sessionOpeningService;
        this.clock = clock;
    }

    // Connecte un visiteur. Chaque chemin calcule une fois Argon2 : le temps de réponse ne révèle
    // donc ni si l'adresse existe, ni si le compte est bloqué (SS §2). Le travail en base, lui, change selon le compte :
    // l'API donne donc la même durée minimale à chaque réponse (voir MinimumAnswerDurationFilter).
    public async Task<AccountLoginResult> LogInAsync(LoginAttempt attempt, CancellationToken cancellationToken = default)
    {
        var normalizedPassword = PasswordRules.Normalize(attempt.Password);
        var account = await FindAccountAsync(attempt.EmailAddress, cancellationToken);

        if (account == null)
        {
            await passwordHasher.HashAsync(normalizedPassword, cancellationToken);
            return await RefuseAsync(null, attempt, cancellationToken);
        }

        if (!account.IsEmailConfirmed)
        {
            return await CheckUnconfirmedAccountAsync(account, normalizedPassword, attempt, cancellationToken);
        }

        return await LogInConfirmedAccountAsync(account, normalizedPassword, attempt, cancellationToken);
    }

    // Cherche le compte. Une adresse mal formée n'est jamais envoyée à la base : aucun compte ne peut l'utiliser,
    // et certains caractères, comme le caractère nul, y provoqueraient une erreur.
    private async Task<User?> FindAccountAsync(string typedEmail, CancellationToken cancellationToken = default)
    {
        var email = EmailAddressRules.Normalize(typedEmail);

        if (!EmailAddressRules.IsValid(email))
        {
            return null;
        }

        return await accountRepository.FindByEmailAsync(email, cancellationToken);
    }

    // Un compte non confirmé n'est jamais bloqué : ses essais ne comptent pas (SS §2).
    // Le résultat qui invite à confirmer n'apparaît qu'avec le bon mot de passe.
    private async Task<AccountLoginResult> CheckUnconfirmedAccountAsync(
        User account,
        string normalizedPassword,
        LoginAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        var passwordMatches = await passwordHasher.VerifyAsync(normalizedPassword, account.PasswordHash, cancellationToken);

        if (!passwordMatches)
        {
            return await RefuseAsync(account.Id, attempt, cancellationToken);
        }

        await securityJournal.SaveEventNowAsync(SecurityEventType.Login, SecurityEventResult.Failure, account.Id, attempt.IpAddress);
        return new AccountLoginResult.EmailAddressNotConfirmed();
    }

    // Vérifie le mot de passe, compte l'essai, puis ouvre la session si tout est bon, ou attend le code.
    // Pendant un blocage, même le bon mot de passe reçoit le refus générique (SS §2).
    // Le mot de passe est comparé avant que l'essai compte : un essai que le serveur saturé n'a pas pu vérifier ne compte pas, et
    // ne peut donc pas bloquer un vrai utilisateur. Un essai vérifié compte toujours, même si le client raccroche ensuite.
    private async Task<AccountLoginResult> LogInConfirmedAccountAsync(
        User account,
        string normalizedPassword,
        LoginAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        var passwordHash = account.PasswordHash;
        var encryptedSecret = account.EncryptedTotpSecret;
        var passwordMatches = await passwordHasher.VerifyAsync(normalizedPassword, passwordHash, cancellationToken);
        var countResult = await CountAttemptAsync(account.Id);

        if (countResult == LoginAttemptCountResult.Refused || !passwordMatches)
        {
            return await RefuseConfirmedAccountAsync(account.Id, countResult, attempt, cancellationToken);
        }

        if (encryptedSecret != null)
        {
            return await StartWaitingForCodeAsync(account.Id, passwordHash, countResult, attempt, cancellationToken);
        }

        return await OpenSessionAsync(account.Id, passwordHash, attempt, cancellationToken);
    }

    // Compte l'essai en base. Si une autre requête a changé le compte entre-temps, le compte est relu et l'essai recompté :
    // ainsi, aucun essai ne se perd, même avec des requêtes simultanées. Le compte ne s'annule pas : un client qui raccroche
    // ne peut pas échapper au blocage en abandonnant ses requêtes.
    private async Task<LoginAttemptCountResult> CountAttemptAsync(Guid accountId)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        return await savingService.SaveWithRetriesAsync(
            () => PrepareAttemptCountAsync(accountId, now, CancellationToken.None),
            LoginAttemptCountResult.Refused,
            CancellationToken.None);
    }

    // Un passage : lit le compte, refuse s'il a disparu ou s'il est bloqué, sinon compte un essai de plus.
    private async Task<PreparedChanges<LoginAttemptCountResult>> PrepareAttemptCountAsync(Guid accountId, DateTime now, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(accountId, cancellationToken);

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
        LoginAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        await NotifyLockoutAsync(accountId, countResult, attempt);

        return await RefuseAsync(accountId, attempt, cancellationToken);
    }

    // Une seule requête peut avoir déclenché le blocage : il n'est donc noté qu'une fois, et un seul lien de déblocage part (SS §2).
    // Ce lien est l'alerte « compte bloqué » (GR Notif) : la trace du journal et la demande d'e-mail partent ensemble.
    // L'e-mail part en arrière-plan : le temps de réponse ne révèle donc pas que le compte existe.
    // Le bon mot de passe sans double authentification n'arrive jamais ici : sa connexion réussie annule le blocage.
    private async Task NotifyLockoutAsync(
        Guid accountId,
        LoginAttemptCountResult countResult,
        LoginAttempt attempt)
    {
        if (countResult != LoginAttemptCountResult.CountedAndLocked)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;

        // La trace du blocage et le lien de déblocage ne s'annulent pas : ils partent même si le client raccroche.
        await savingService.SaveWithRetriesAsync(() => PrepareLockoutNoticeAsync(accountId, attempt, now, CancellationToken.None), false, CancellationToken.None);
    }

    // Un passage : prépare la trace du blocage au journal et la demande du lien de déblocage. Rien d'existant n'est modifié :
    // seul l'effacement du compte au même instant empêche cet enregistrement (GR Suppression). Le passage suivant ne trouve
    // alors plus de compte à prévenir.
    private async Task<PreparedChanges<bool>> PrepareLockoutNoticeAsync(Guid accountId, LoginAttempt attempt, DateTime now, CancellationToken cancellationToken = default)
    {
        if (await accountRepository.FindByIdAsync(accountId, cancellationToken) == null)
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        securityJournal.PrepareEvent(SecurityEventType.Lockout, SecurityEventResult.Success, accountId, attempt.IpAddress);
        emailRequests.PrepareRequest(EmailRequest.ForAccountUnlock(accountId, attempt.IpAddress, now));

        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Le mot de passe est bon, mais le compte demande aussi un code : aucune session ne s'ouvre, et l'essai reste compté.
    // Seul le bon code remet les compteurs à zéro : un attaquant qui a volé le mot de passe finit donc bloqué, et le propriétaire est prévenu (GR Auth).
    // Si cet essai vient de bloquer le compte, le code reste permis : le vrai propriétaire retrouve l'accès, et ce blocage disparaît.
    private async Task<AccountLoginResult> StartWaitingForCodeAsync(
        Guid accountId,
        string passwordHash,
        LoginAttemptCountResult countResult,
        LoginAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        await NotifyLockoutAsync(accountId, countResult, attempt);

        var waitingLoginToken = tokenGenerator.GenerateToken();
        waitingLoginStore.Save(waitingLoginToken.Hash, accountId, passwordHash);

        return new AccountLoginResult.WaitingForTwoFactorCode { WaitingLoginToken = waitingLoginToken.Token };
    }

    // Ouvre la session d'un compte sans double authentification. Si elle vient d'être activée, la connexion est refusée.
    private async Task<AccountLoginResult> OpenSessionAsync(
        Guid accountId,
        string passwordHash,
        LoginAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionOpeningService.TryOpenSessionAsync(new SuccessfulLogin
        {
            AccountId = accountId,
            PasswordHash = passwordHash,
            IpAddress = attempt.IpAddress,
            DeviceToken = attempt.DeviceToken,
            SessionToken = attempt.SessionToken
        },
        cancellationToken);

        if (session == null)
        {
            return await RefuseAsync(accountId, attempt, cancellationToken);
        }

        return new AccountLoginResult.LoggedIn
        {
            SessionToken = session.SessionToken,
            DeviceToken = session.DeviceToken,
            HasCancelledDeletion = session.HasCancelledDeletion
        };
    }

    // Note l'échec dans le journal, et donne le même refus dans tous les cas (GR Auth).
    private async Task<AccountLoginResult> RefuseAsync(Guid? accountId, LoginAttempt attempt, CancellationToken cancellationToken = default)
    {
        await securityJournal.SaveEventNowAsync(SecurityEventType.Login, SecurityEventResult.Failure, accountId, attempt.IpAddress);

        return new AccountLoginResult.WrongCredentials();
    }
}
