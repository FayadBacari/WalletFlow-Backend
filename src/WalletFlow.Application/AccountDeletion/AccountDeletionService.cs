using System.Net;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountDeletion;

// Cas d'utilisation : l'utilisateur connecté demande la suppression de son compte (GR Suppression, SS §2 et §7). C'est une
// action sensible : elle redemande le mot de passe, et un code si la double authentification est activée. En une seule
// transaction, la demande ferme toutes les sessions du compte, annule ses liens en attente et confirme la demande par e-mail ;
// puis l'autorisation de chaque banque est révoquée chez le prestataire. Le compte sera effacé 30 jours après, sauf si
// l'utilisateur se reconnecte avant. Chaque demande est notée au journal, et chaque refus aussi (SS §11).
public class AccountDeletionService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IUserSessionRepository sessionRepository;
    private readonly IEmailRequestRepository emailRequests;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly AccountIdentityCheckingService identityChecking;
    private readonly DeletedAccountBankRevocation bankRevocation;
    private readonly TimeProvider clock;

    // Reçoit les outils dont la demande a besoin.
    public AccountDeletionService(
        IUserAccountRepository accountRepository,
        IUserSessionRepository sessionRepository,
        IEmailRequestRepository emailRequests,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        AccountIdentityCheckingService identityChecking,
        DeletedAccountBankRevocation bankRevocation,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.sessionRepository = sessionRepository;
        this.emailRequests = emailRequests;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.identityChecking = identityChecking;
        this.bankRevocation = bankRevocation;
        this.clock = clock;
    }

    // Vérifie l'identité, enregistre la demande avec tout ce qu'elle ferme, puis révoque les banques. Une révocation que le
    // prestataire ne confirme pas ne bloque pas la demande : la tâche de fond la réessaiera.
    public async Task<AccountDeletionResult> RequestAsync(AccountDeletionAttempt attempt, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(attempt.UserId, cancellationToken);
        if (account == null)
        {
            return new AccountDeletionResult.AccountGone();
        }

        // Lus avant la vérification : le passage qui enregistre contrôle qu'ils sont toujours les bons.
        var checkedPasswordHash = account.PasswordHash;
        var checkedTotpSecret = account.EncryptedTotpSecret;

        var identityCheck = await identityChecking.CheckAsync(account, attempt.Password, attempt.Code, cancellationToken);
        if (!identityCheck.IsConfirmed)
        {
            return await RefuseAsync(account, attempt.IpAddress, cancellationToken);
        }

        var deletion = new DeletionToRecord
        {
            UserId = account.Id,
            CheckedPasswordHash = checkedPasswordHash,
            CheckedTotpSecret = checkedTotpSecret,
            BackupCodeId = identityCheck.BackupCodeId,
            AuthenticatorStep = identityCheck.AuthenticatorStep,
            IpAddress = attempt.IpAddress
        };

        var erasesAt = await savingService.SaveWithRetriesAsync<DateTime?>(() => PrepareDeletionAsync(deletion, cancellationToken), null, cancellationToken);
        if (erasesAt == null)
        {
            return await RefuseAsync(account, attempt.IpAddress, cancellationToken);
        }

        await bankRevocation.RevokeAccountConnectionsAsync(account.Id, attempt.IpAddress, cancellationToken);

        return new AccountDeletionResult.Requested { ErasesAt = erasesAt.Value };
    }

    // Un passage : relit le compte et vérifie que le mot de passe et le secret vérifiés sont toujours les siens. Une demande déjà
    // enregistrée par une autre requête n'est pas refaite : sa date d'effacement est rendue telle quelle. Sinon, prépare la
    // demande, l'effacement des sessions et des liens, le code utilisé s'il y en a un, l'e-mail de
    // confirmation et l'événement du journal. Le compte est contrôlé à l'enregistrement : une connexion ou un changement de mot
    // de passe au même instant fait recommencer le passage. Donne null si la demande ne peut plus être faite.
    private async Task<PreparedChanges<DateTime?>> PrepareDeletionAsync(DeletionToRecord deletion, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(deletion.UserId, cancellationToken);
        if (account == null
            || account.PasswordHash != deletion.CheckedPasswordHash
            || account.EncryptedTotpSecret != deletion.CheckedTotpSecret)
        {
            return PreparedChanges<DateTime?>.NothingToSave(null);
        }

        if (account.DeletionRequestedAt is DateTime alreadyRequestedAt)
        {
            return PreparedChanges<DateTime?>.NothingToSave(AccountDeletionRules.FindErasureMoment(alreadyRequestedAt));
        }

        if (!await identityChecking.TryPrepareCodeUseAsync(account, deletion.BackupCodeId, deletion.AuthenticatorStep, deletion.IpAddress, cancellationToken))
        {
            return PreparedChanges<DateTime?>.NothingToSave(null);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        account.RequestDeletion(now);
        accountRepository.PrepareUnchangedCheck(account);
        await sessionRepository.PrepareAllSessionsRemovalAsync(account.Id, cancellationToken);
        await accountRepository.PrepareAllLinksRemovalAsync(account.Id, cancellationToken);
        emailRequests.PrepareRequest(EmailRequest.ForDeletionRequest(account.Id, deletion.IpAddress, now));
        securityJournal.PrepareEvent(SecurityEventType.DeletionRequest, SecurityEventResult.Success, account.Id, deletion.IpAddress);

        return PreparedChanges<DateTime?>.SaveThenReturn(AccountDeletionRules.FindErasureMoment(now));
    }

    // Note le refus dans le journal. Avec la double authentification, un mauvais mot de passe et un mauvais code reçoivent le même refus.
    private async Task<AccountDeletionResult> RefuseAsync(User account, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        await securityJournal.SaveEventNowAsync(SecurityEventType.DeletionRequest, SecurityEventResult.Failure, account.Id, ipAddress);

        if (account.IsTwoFactorEnabled)
        {
            return new AccountDeletionResult.WrongPasswordOrCode();
        }

        return new AccountDeletionResult.WrongPassword();
    }

    // Ce qu'un passage doit enregistrer, et ce qui a été vérifié avant.
    private sealed class DeletionToRecord
    {
        public required Guid UserId { get; init; }
        public required string CheckedPasswordHash { get; init; }
        public required string? CheckedTotpSecret { get; init; }
        public required Guid? BackupCodeId { get; init; }
        public required long? AuthenticatorStep { get; init; }
        public required IPAddress? IpAddress { get; init; }
    }
}
