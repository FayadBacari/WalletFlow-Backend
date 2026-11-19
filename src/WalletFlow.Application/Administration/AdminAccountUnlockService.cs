using System.Net;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Administration;

// Cas d'utilisation : l'administrateur débloque un compte (GR Admin). Le déblocage fait comme le lien de déblocage : il termine
// le blocage et redonne ses 5 essais au compte, sans connecter personne (SS §2). Il est noté deux fois au journal : comme un
// déblocage du compte, et comme une action de l'administrateur sur ce compte (SS §11).
public class AdminAccountUnlockService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux comptes, l'enregistrement avec réessai, le journal de sécurité et l'horloge.
    public AdminAccountUnlockService(
        IUserAccountRepository accountRepository,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.clock = clock;
    }

    // Débloque le compte s'il est bloqué. Si une autre requête change le compte entre-temps, il est relu et tout recommence.
    public async Task<AdminAccountUnlockResult> UnlockAsync(Guid administratorId, Guid accountId, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        return await savingService.SaveWithRetriesAsync(
            () => PrepareUnlockAsync(administratorId, accountId, ipAddress, cancellationToken),
            new AdminAccountUnlockResult.NotSaved(),
            cancellationToken);
    }

    // Un passage : lit le compte, vérifie qu'il est bloqué, puis prépare le déblocage et ses deux événements du journal.
    private async Task<PreparedChanges<AdminAccountUnlockResult>> PrepareUnlockAsync(Guid administratorId, Guid accountId, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(accountId, cancellationToken);

        if (account == null)
        {
            return PreparedChanges<AdminAccountUnlockResult>.NothingToSave(new AdminAccountUnlockResult.NotFound());
        }

        var now = clock.GetUtcNow().UtcDateTime;

        if (!account.IsLockedAt(now))
        {
            return PreparedChanges<AdminAccountUnlockResult>.NothingToSave(new AdminAccountUnlockResult.NotLocked());
        }

        account.Unlock();
        securityJournal.PrepareEvent(SecurityEventType.Unlock, SecurityEventResult.Success, account.Id, ipAddress);
        securityJournal.PrepareAdminAction(administratorId, account.Id, ipAddress);

        return PreparedChanges<AdminAccountUnlockResult>.SaveThenReturn(
            new AdminAccountUnlockResult.Unlocked { Account = AdminAccountView.Create(account, now) });
    }
}
