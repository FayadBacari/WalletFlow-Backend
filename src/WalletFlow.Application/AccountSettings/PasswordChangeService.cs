using System.Net;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountSettings;

// Cas d'utilisation : l'utilisateur connecté change son mot de passe (GR Compte, SS §2 et §3).
// Le nouveau mot de passe, la fermeture de toutes les sessions et la nouvelle session en cours partent ensemble, en une transaction :
// aucune session ne peut survivre au changement, même si le serveur s'arrête au mauvais moment.
public class PasswordChangeService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IUserSessionRepository sessionRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly AccountIdentityCheckingService identityChecking;
    private readonly IPasswordHasher passwordHasher;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly TimeProvider clock;

    // Reçoit les outils dont le changement de mot de passe a besoin.
    public PasswordChangeService(
        IUserAccountRepository accountRepository,
        IUserSessionRepository sessionRepository,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        AccountIdentityCheckingService identityChecking,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.sessionRepository = sessionRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.identityChecking = identityChecking;
        this.passwordHasher = passwordHasher;
        this.tokenGenerator = tokenGenerator;
        this.clock = clock;
    }

    // Change le mot de passe. Les règles du nouveau mot de passe passent d'abord : ce contrôle ne dit rien du compte,
    // et un mot de passe refusé ne demande aucun hachage. Le même mot de passe que l'actuel est accepté.
    public async Task<PasswordChangeResult> ChangeAsync(PasswordChangeAttempt attempt)
    {
        var normalizedNewPassword = PasswordRules.Normalize(attempt.NewPassword);
        if (!PasswordRules.IsValid(normalizedNewPassword))
        {
            return new PasswordChangeResult.NewPasswordBreaksRules();
        }

        var account = await accountRepository.FindByIdAsync(attempt.UserId);
        if (account == null)
        {
            return new PasswordChangeResult.WrongPassword();
        }

        // Lus avant la vérification : l'enregistrement contrôlera que le mot de passe et le secret vérifiés sont toujours les bons.
        var checkedPasswordHash = account.PasswordHash;
        var checkedTotpSecret = account.EncryptedTotpSecret;

        var identityCheck = await identityChecking.CheckAsync(account, attempt.CurrentPassword, attempt.Code);
        if (!identityCheck.IsConfirmed)
        {
            return await RefuseAsync(account, attempt.IpAddress);
        }

        var newSessionToken = tokenGenerator.GenerateToken();
        var change = new NewPasswordToSave
        {
            UserId = account.Id,
            CheckedPasswordHash = checkedPasswordHash,
            CheckedTotpSecret = checkedTotpSecret,
            BackupCodeId = identityCheck.BackupCodeId,
            NewPasswordHash = await passwordHasher.HashAsync(normalizedNewPassword),
            NewSessionTokenHash = newSessionToken.Hash,
            IpAddress = attempt.IpAddress
        };

        var saved = await savingService.SaveWithRetriesAsync(() => PrepareNewPasswordAsync(change), false);
        if (!saved)
        {
            return await RefuseAsync(account, attempt.IpAddress);
        }

        return new PasswordChangeResult.Changed { SessionToken = newSessionToken.Token };
    }

    // Un passage : lit le compte, vérifie que ce qui vient d'être contrôlé est toujours valable, puis prépare en une fois
    // le nouveau mot de passe, le code de secours utilisé s'il y en a un, la fermeture de toutes les sessions,
    // la nouvelle session en cours et l'événement du journal. Donne faux si une récupération ou un autre changement est passé.
    private async Task<PreparedChanges<bool>> PrepareNewPasswordAsync(NewPasswordToSave change)
    {
        var account = await accountRepository.FindByIdAsync(change.UserId);
        if (account == null
            || account.PasswordHash != change.CheckedPasswordHash
            || account.EncryptedTotpSecret != change.CheckedTotpSecret)
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        if (!await identityChecking.TryPrepareBackupCodeUseAsync(change.BackupCodeId))
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        account.ReplacePasswordHash(change.NewPasswordHash);
        await sessionRepository.PrepareAllSessionsRemovalAsync(change.UserId);
        sessionRepository.PrepareSession(new UserSession
        {
            Id = change.NewSessionTokenHash,
            LastActivityAt = clock.GetUtcNow().UtcDateTime,
            UserId = change.UserId
        });
        securityJournal.PrepareEvent(SecurityEventType.PasswordChange, SecurityEventResult.Success, change.UserId, change.IpAddress);

        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Note le refus dans le journal. Avec la double authentification, un mauvais mot de passe et un mauvais code reçoivent
    // le même refus : une session volée ne peut pas servir à tester des mots de passe un par un.
    private async Task<PasswordChangeResult> RefuseAsync(User account, IPAddress? ipAddress)
    {
        await securityJournal.SaveEventNowAsync(SecurityEventType.PasswordChange, SecurityEventResult.Failure, account.Id, ipAddress);

        if (account.IsTwoFactorEnabled)
        {
            return new PasswordChangeResult.WrongPasswordOrCode();
        }

        return new PasswordChangeResult.WrongPassword();
    }

    // Ce qu'un passage doit enregistrer, et ce qui a été vérifié avant.
    private class NewPasswordToSave
    {
        public required Guid UserId { get; init; }
        public required string CheckedPasswordHash { get; init; }
        public required string? CheckedTotpSecret { get; init; }
        public required Guid? BackupCodeId { get; init; }
        public required string NewPasswordHash { get; init; }
        public required string NewSessionTokenHash { get; init; }
        public required IPAddress? IpAddress { get; init; }
    }
}
