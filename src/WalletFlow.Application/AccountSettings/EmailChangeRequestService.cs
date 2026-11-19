using System.Net;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.NotificationsAndSupport;
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
    private readonly IEmailRequestRepository emailRequests;
    private readonly TimeProvider clock;

    // Reçoit les outils dont la demande a besoin.
    public EmailChangeRequestService(
        IUserAccountRepository accountRepository,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        AccountIdentityCheckingService identityChecking,
        IEmailChangeRequestLimiter requestLimiter,
        IEmailRequestRepository emailRequests,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.identityChecking = identityChecking;
        this.requestLimiter = requestLimiter;
        this.emailRequests = emailRequests;
        this.clock = clock;
    }

    // Vérifie la nouvelle adresse, l'identité et la limite, puis confie les e-mails à la file.
    public async Task<EmailChangeRequestResult> RequestAsync(EmailChangeAttempt attempt, CancellationToken cancellationToken = default)
    {
        var newEmail = EmailAddressRules.Normalize(attempt.NewEmail);
        if (!EmailAddressRules.IsValid(newEmail))
        {
            return new EmailChangeRequestResult.InvalidEmailAddress();
        }

        var account = await accountRepository.FindByIdAsync(attempt.UserId, cancellationToken);
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

        var identityCheck = await identityChecking.CheckAsync(account, attempt.Password, attempt.Code, cancellationToken);
        if (!identityCheck.IsConfirmed)
        {
            return await RefuseAsync(account, attempt.IpAddress, cancellationToken);
        }

        // La limite par adresse ne compte que les demandes du propriétaire du compte : une session volée ne peut pas l'épuiser (SS §6).
        if (!requestLimiter.TryCountRequest(newEmail))
        {
            return new EmailChangeRequestResult.TooManyRequests();
        }

        var change = new EmailChangeToRequest
        {
            UserId = account.Id,
            NewEmail = newEmail,
            CheckedPasswordHash = checkedPasswordHash,
            CheckedTotpSecret = checkedTotpSecret,
            BackupCodeId = identityCheck.BackupCodeId,
            AuthenticatorStep = identityCheck.AuthenticatorStep,
            IpAddress = attempt.IpAddress
        };

        if (!await savingService.SaveWithRetriesAsync(() => PrepareRequestAsync(change, cancellationToken), false, cancellationToken))
        {
            return await RefuseAsync(account, attempt.IpAddress, cancellationToken);
        }

        return new EmailChangeRequestResult.RequestAccepted();
    }

    // Un passage : prépare l'entrée de la demande dans la file d'e-mails. Si un code a servi, relit d'abord le compte, vérifie que
    // le mot de passe et le secret contrôlés sont toujours les bons, et prépare l'usage du code : le code n'est dépensé que si la
    // demande entre dans la file. Le compte est alors contrôlé à l'enregistrement, même s'il n'a pas changé.
    private async Task<PreparedChanges<bool>> PrepareRequestAsync(EmailChangeToRequest change, CancellationToken cancellationToken = default)
    {
        if (change.BackupCodeId != null || change.AuthenticatorStep != null)
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

            accountRepository.PrepareUnchangedCheck(account);
        }

        emailRequests.PrepareRequest(EmailRequest.ForEmailChange(
            change.UserId,
            change.NewEmail,
            change.IpAddress,
            clock.GetUtcNow().UtcDateTime));

        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Note le refus dans le journal. Avec la double authentification, un mauvais mot de passe et un mauvais code reçoivent le même refus.
    private async Task<EmailChangeRequestResult> RefuseAsync(User account, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        await securityJournal.SaveEventNowAsync(SecurityEventType.EmailChange, SecurityEventResult.Failure, account.Id, ipAddress);

        if (account.IsTwoFactorEnabled)
        {
            return new EmailChangeRequestResult.WrongPasswordOrCode();
        }

        return new EmailChangeRequestResult.WrongPassword();
    }

    // Ce qu'un passage doit enregistrer, et ce qui a été vérifié avant.
    private sealed class EmailChangeToRequest
    {
        public required Guid UserId { get; init; }
        public required string NewEmail { get; init; }
        public required string CheckedPasswordHash { get; init; }
        public required string? CheckedTotpSecret { get; init; }
        public required Guid? BackupCodeId { get; init; }
        public required long? AuthenticatorStep { get; init; }
        public required IPAddress? IpAddress { get; init; }
    }
}
