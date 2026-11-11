using System.Net;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountRecovery;

// Cas d'utilisation : un utilisateur débloque son compte avec le lien reçu par e-mail (GR Auth, SS §2).
// Le lien remet les essais à zéro et termine le blocage, mais il ne connecte personne.
public class AccountUnlockService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly TimeProvider clock;

    // Reçoit les outils dont le déblocage a besoin.
    public AccountUnlockService(
        IUserAccountRepository accountRepository,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        ISecureTokenGenerator tokenGenerator,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.tokenGenerator = tokenGenerator;
        this.clock = clock;
    }

    // Débloque le compte si le lien est encore valable. Si une autre requête a changé le compte ou le lien,
    // tout est relu : un lien déjà utilisé ou remplacé entre-temps est alors refusé.
    public async Task<AccountUnlockResult> UnlockAsync(string linkToken, IPAddress? ipAddress)
    {
        var tokenHash = tokenGenerator.HashToken(linkToken);

        return await savingService.SaveWithRetriesAsync(
            () => PrepareUnlockAsync(tokenHash, ipAddress),
            AccountUnlockResult.LinkNoLongerValid);
    }

    // Un passage : lit le lien et son compte, vérifie le lien, puis prépare le déblocage, le lien utilisé et l'événement du journal.
    // Tout lien qui ne vaut plus reçoit le même refus, sans dire pourquoi.
    private async Task<PreparedChanges<AccountUnlockResult>> PrepareUnlockAsync(string tokenHash, IPAddress? ipAddress)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var token = await accountRepository.FindTokenAsync(tokenHash, EmailTokenType.AccountUnlock);

        if (token == null || !token.CanBeUsedAt(now))
        {
            return PreparedChanges<AccountUnlockResult>.NothingToSave(AccountUnlockResult.LinkNoLongerValid);
        }

        var account = await accountRepository.FindByIdAsync(token.UserId);
        if (account == null)
        {
            return PreparedChanges<AccountUnlockResult>.NothingToSave(AccountUnlockResult.LinkNoLongerValid);
        }

        account.Unlock();
        token.MarkAsUsed(now);
        securityJournal.PrepareEvent(SecurityEventType.Unlock, SecurityEventResult.Success, account.Id, ipAddress);

        return PreparedChanges<AccountUnlockResult>.SaveThenReturn(AccountUnlockResult.AccountUnlocked);
    }
}
