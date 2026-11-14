using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountSettings;

// Cas d'utilisation : l'utilisateur connecté demande à changer son adresse e-mail (GR Compte, SS §2).
// La réponse est la même que la nouvelle adresse soit libre ou déjà utilisée : les e-mails partent en arrière-plan,
// et ni leur contenu ni leur temps d'envoi n'apparaissent dans la réponse. L'adresse ne change qu'à la confirmation.
public class EmailChangeRequestService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly AccountIdentityCheckingService identityChecking;
    private readonly IEmailChangeRequestLimiter requestLimiter;
    private readonly IBackgroundEmailRequestQueue emailQueue;

    // Reçoit les outils dont la demande a besoin.
    public EmailChangeRequestService(
        IUserAccountRepository accountRepository,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        AccountIdentityCheckingService identityChecking,
        IEmailChangeRequestLimiter requestLimiter,
        IBackgroundEmailRequestQueue emailQueue)
    {
        this.accountRepository = accountRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.identityChecking = identityChecking;
        this.requestLimiter = requestLimiter;
        this.emailQueue = emailQueue;
    }

    // Vérifie la nouvelle adresse, l'identité et la limite, puis confie les e-mails à la file.
    public async Task<EmailChangeRequestResult> RequestAsync(EmailChangeAttempt attempt)
    {
        var newEmail = EmailAddressRules.Normalize(attempt.NewEmail);
        if (!EmailAddressRules.IsValid(newEmail))
        {
            return new EmailChangeRequestResult.InvalidEmailAddress();
        }

        var account = await accountRepository.FindByIdAsync(attempt.UserId);
        if (account == null)
        {
            return new EmailChangeRequestResult.WrongPassword();
        }

        // C'est déjà l'adresse du compte : le dire ne révèle rien, l'utilisateur la connaît.
        if (account.Email == newEmail)
        {
            return new EmailChangeRequestResult.SameEmailAddress();
        }

        // Lus avant la vérification : l'usage d'un code de secours contrôlera qu'ils sont toujours les bons.
        var checkedPasswordHash = account.PasswordHash;
        var checkedTotpSecret = account.EncryptedTotpSecret;

        var identityCheck = await identityChecking.CheckAsync(account, attempt.Password, attempt.Code);
        if (!identityCheck.IsConfirmed)
        {
            return await RefuseAsync(account, attempt.IpAddress);
        }

        // La limite par adresse ne compte que les demandes du propriétaire du compte : une session volée ne peut pas l'épuiser (SS §6).
        if (!requestLimiter.TryCountRequest(newEmail))
        {
            return new EmailChangeRequestResult.TooManyRequests();
        }

        if (identityCheck.BackupCodeId != null)
        {
            var saved = await savingService.SaveWithRetriesAsync(
                () => PrepareBackupCodeUseAsync(account.Id, checkedPasswordHash, checkedTotpSecret, identityCheck.BackupCodeId),
                false);

            if (!saved)
            {
                return await RefuseAsync(account, attempt.IpAddress);
            }
        }

        emailQueue.Add(new BackgroundEmailRequest.EmailChange
        {
            AccountId = account.Id,
            NewEmailAddress = newEmail,
            IpAddress = attempt.IpAddress
        });

        return new EmailChangeRequestResult.RequestAccepted();
    }

    // Un passage : lit le compte, vérifie que le mot de passe et le secret contrôlés sont toujours les bons, puis prépare l'usage
    // du code de secours. Le compte est contrôlé à l'enregistrement, même s'il n'a pas changé.
    private async Task<PreparedChanges<bool>> PrepareBackupCodeUseAsync(
        Guid userId,
        string checkedPasswordHash,
        string? checkedTotpSecret,
        Guid? backupCodeId)
    {
        var account = await accountRepository.FindByIdAsync(userId);
        if (account == null
            || account.PasswordHash != checkedPasswordHash
            || account.EncryptedTotpSecret != checkedTotpSecret)
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        if (!await identityChecking.TryPrepareBackupCodeUseAsync(backupCodeId))
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        accountRepository.PrepareUnchangedCheck(account);

        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Note le refus dans le journal. Avec la double authentification, un mauvais mot de passe et un mauvais code reçoivent le même refus.
    private async Task<EmailChangeRequestResult> RefuseAsync(User account, IPAddress? ipAddress)
    {
        await securityJournal.SaveEventNowAsync(SecurityEventType.EmailChange, SecurityEventResult.Failure, account.Id, ipAddress);

        if (account.IsTwoFactorEnabled)
        {
            return new EmailChangeRequestResult.WrongPasswordOrCode();
        }

        return new EmailChangeRequestResult.WrongPassword();
    }
}
