using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountDeletion;

// Cas d'utilisation de la tâche de fond : efface définitivement les comptes dont la suppression est demandée depuis 30 jours
// (GR Suppression, SS §7). Chaque effacement a sa propre transaction, avec l'e-mail qui le confirme : il garde l'adresse du
// compte jusqu'à son envoi. La base efface tout ce qui appartient au compte, et le journal garde ses événements sans lui.
public class AccountErasureService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IEmailRequestRepository emailRequests;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux comptes, la file d'e-mails, l'enregistrement et l'horloge.
    public AccountErasureService(
        IUserAccountRepository accountRepository,
        IEmailRequestRepository emailRequests,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.emailRequests = emailRequests;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Donne les comptes dont la suppression est demandée depuis 30 jours ou plus.
    public async Task<List<Guid>> FindAccountsToEraseAsync()
    {
        var now = clock.GetUtcNow().UtcDateTime;

        return await accountRepository.FindAccountsToEraseAsync(now - AccountDeletionRules.ErasureDelay);
    }

    // Efface ce compte, avec l'e-mail qui le confirme, si sa suppression est toujours demandée depuis 30 jours. Renvoie faux si
    // le compte n'est plus à effacer : une connexion a annulé la demande, ou il est déjà effacé.
    public async Task<bool> EraseAsync(Guid userId)
    {
        return await savingService.SaveWithRetriesAsync(() => PrepareErasureAsync(userId), false);
    }

    // Un passage : relit le compte, vérifie que son effacement est dû, puis le prépare avec l'e-mail. Le compte est effacé en
    // vérifiant sa version : une connexion qui annule la demande au même instant fait recommencer le passage, qui ne trouve
    // alors plus rien à effacer.
    private async Task<PreparedChanges<bool>> PrepareErasureAsync(Guid userId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var account = await accountRepository.FindByIdAsync(userId);

        if (account == null || !account.IsDueForErasureAt(now))
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        accountRepository.PrepareErasure(account);
        emailRequests.PrepareRequest(EmailRequest.ForAccountErasure(account.Email, now));

        return PreparedChanges<bool>.SaveThenReturn(true);
    }
}
