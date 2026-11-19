using System.Net;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountSettings;

// Cas d'utilisation : l'utilisateur connecté change son mot de passe (GR Compte, SS §2 et §3).
// Le nouveau mot de passe, la fermeture de toutes les sessions et la nouvelle session en cours partent ensemble, en une transaction :
// aucune session ne peut survivre au changement, même si le serveur s'arrête au mauvais moment.
public class PasswordChangeService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IUserSessionRepository sessionRepository;
    private readonly IEmailRequestRepository emailRequests;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly AccountIdentityCheckingService identityChecking;
    private readonly IPasswordHasher passwordHasher;
    private readonly IBreachedPasswordChecker breachedPasswordChecker;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly TimeProvider clock;

    // Reçoit les outils dont le changement de mot de passe a besoin.
    public PasswordChangeService(
        IUserAccountRepository accountRepository,
        IUserSessionRepository sessionRepository,
        IEmailRequestRepository emailRequests,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        AccountIdentityCheckingService identityChecking,
        IPasswordHasher passwordHasher,
        IBreachedPasswordChecker breachedPasswordChecker,
        ISecureTokenGenerator tokenGenerator,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.sessionRepository = sessionRepository;
        this.emailRequests = emailRequests;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.identityChecking = identityChecking;
        this.passwordHasher = passwordHasher;
        this.breachedPasswordChecker = breachedPasswordChecker;
        this.tokenGenerator = tokenGenerator;
        this.clock = clock;
    }

    // Change le mot de passe. Les règles du nouveau mot de passe passent d'abord : ce contrôle ne dit rien du compte,
    // et un mot de passe refusé ne demande aucun hachage. Le même mot de passe que l'actuel est accepté.
    public async Task<PasswordChangeResult> ChangeAsync(PasswordChangeAttempt attempt, CancellationToken cancellationToken = default)
    {
        var normalizedNewPassword = PasswordRules.Normalize(attempt.NewPassword);
        if (!PasswordRules.IsValid(normalizedNewPassword))
        {
            return new PasswordChangeResult.NewPasswordBreaksRules();
        }

        if (await breachedPasswordChecker.IsBreachedAsync(normalizedNewPassword, cancellationToken))
        {
            return new PasswordChangeResult.NewPasswordBreached();
        }

        var account = await accountRepository.FindByIdAsync(attempt.UserId, cancellationToken);
        if (account == null)
        {
            return new PasswordChangeResult.WrongPassword();
        }

        // Lus avant la vérification : l'enregistrement contrôlera que le mot de passe et le secret vérifiés sont toujours les bons.
        var checkedPasswordHash = account.PasswordHash;
        var checkedTotpSecret = account.EncryptedTotpSecret;

        var identityCheck = await identityChecking.CheckAsync(account, attempt.CurrentPassword, attempt.Code, cancellationToken);
        if (!identityCheck.IsConfirmed)
        {
            return await RefuseAsync(account, attempt.IpAddress, cancellationToken);
        }

        var newSessionToken = tokenGenerator.GenerateToken();
        var change = new NewPasswordToSave
        {
            UserId = account.Id,
            CheckedPasswordHash = checkedPasswordHash,
            CheckedTotpSecret = checkedTotpSecret,
            BackupCodeId = identityCheck.BackupCodeId,
            AuthenticatorStep = identityCheck.AuthenticatorStep,
            NewPasswordHash = await passwordHasher.HashAsync(normalizedNewPassword, cancellationToken),
            NewSessionTokenHash = newSessionToken.Hash,
            IpAddress = attempt.IpAddress
        };

        var saved = await savingService.SaveWithRetriesAsync(() => PrepareNewPasswordAsync(change, cancellationToken), false, cancellationToken);
        if (!saved)
        {
            return await RefuseAsync(account, attempt.IpAddress, cancellationToken);
        }

        return new PasswordChangeResult.Changed { SessionToken = newSessionToken.Token };
    }

    // Un passage : lit le compte, vérifie que ce qui vient d'être contrôlé est toujours valable, puis prépare en une fois
    // le nouveau mot de passe, le code utilisé s'il y en a un, la fermeture de toutes les sessions,
    // la nouvelle session en cours, l'événement du journal et l'alerte du changement (GR Notif).
    // Donne faux si une récupération ou un autre changement est passé.
    private async Task<PreparedChanges<bool>> PrepareNewPasswordAsync(NewPasswordToSave change, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(change.UserId, cancellationToken);
        if (account == null
            || account.PasswordHash != change.CheckedPasswordHash
            || account.EncryptedTotpSecret != change.CheckedTotpSecret)
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        if (!await identityChecking.TryPrepareCodeUseAsync(account, change.BackupCodeId, change.AuthenticatorStep, change.IpAddress, cancellationToken))
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        var now = clock.GetUtcNow().UtcDateTime;

        account.ReplacePasswordHash(change.NewPasswordHash);
        await sessionRepository.PrepareAllSessionsRemovalAsync(change.UserId, cancellationToken);
        // La nouvelle session repart pour 12 heures : le mot de passe vient d'être redemandé.
        sessionRepository.PrepareSession(new UserSession
        {
            Id = change.NewSessionTokenHash,
            CreatedAt = now,
            LastActivityAt = now,
            UserId = change.UserId
        });
        securityJournal.PrepareEvent(SecurityEventType.PasswordChange, SecurityEventResult.Success, change.UserId, change.IpAddress);
        emailRequests.PrepareRequest(EmailRequest.ForPasswordChange(change.UserId, change.IpAddress, now));

        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Note le refus dans le journal. Avec la double authentification, un mauvais mot de passe et un mauvais code reçoivent
    // le même refus : une session volée ne peut pas servir à tester des mots de passe un par un.
    private async Task<PasswordChangeResult> RefuseAsync(User account, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        await securityJournal.SaveEventNowAsync(SecurityEventType.PasswordChange, SecurityEventResult.Failure, account.Id, ipAddress);

        if (account.IsTwoFactorEnabled)
        {
            return new PasswordChangeResult.WrongPasswordOrCode();
        }

        return new PasswordChangeResult.WrongPassword();
    }

    // Ce qu'un passage doit enregistrer, et ce qui a été vérifié avant.
    private sealed class NewPasswordToSave
    {
        public required Guid UserId { get; init; }
        public required string CheckedPasswordHash { get; init; }
        public required string? CheckedTotpSecret { get; init; }
        public required Guid? BackupCodeId { get; init; }
        public required long? AuthenticatorStep { get; init; }
        public required string NewPasswordHash { get; init; }
        public required string NewSessionTokenHash { get; init; }
        public required IPAddress? IpAddress { get; init; }
    }
}
