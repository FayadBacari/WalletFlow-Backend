using System.Net;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Administration;

// Cas d'utilisation : l'administrateur fait envoyer à l'adresse d'un compte le lien qui désactive sa double authentification,
// pour un utilisateur qui a perdu son téléphone et ses codes de secours (GR Admin, GR Auth). Il ne voit ni ce lien ni le mot de
// passe : le lien n'est créé qu'au moment de l'envoi. Si le mot de passe a changé il y a moins de 7 jours, le lien ne marcherait
// pas : l'administrateur en est prévenu, et rien ne part (SS §2). La règle est vérifiée encore quand le lien sert.
public class AdminTwoFactorLinkService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly ISecurityEventRepository securityEvents;
    private readonly IEmailRequestRepository emailRequests;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux comptes et au journal, la file d'e-mails, l'enregistrement avec réessai, le journal et l'horloge.
    public AdminTwoFactorLinkService(
        IUserAccountRepository accountRepository,
        ISecurityEventRepository securityEvents,
        IEmailRequestRepository emailRequests,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.securityEvents = securityEvents;
        this.emailRequests = emailRequests;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.clock = clock;
    }

    // Met le lien dans la file d'e-mails, si le compte a la double authentification et un mot de passe d'au moins 7 jours.
    public async Task<AdminTwoFactorLinkResult> SendAsync(Guid administratorId, Guid accountId, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        return await savingService.SaveWithRetriesAsync(
            () => PrepareLinkAsync(administratorId, accountId, ipAddress, cancellationToken),
            new AdminTwoFactorLinkResult.NotSaved(),
            cancellationToken);
    }

    // Un passage : lit le compte et la date de son dernier changement de mot de passe, puis prépare l'e-mail du lien et l'action
    // de l'administrateur. Le compte est contrôlé à l'enregistrement : un changement entre-temps fait tout recommencer.
    private async Task<PreparedChanges<AdminTwoFactorLinkResult>> PrepareLinkAsync(Guid administratorId, Guid accountId, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(accountId, cancellationToken);

        if (account == null)
        {
            return PreparedChanges<AdminTwoFactorLinkResult>.NothingToSave(new AdminTwoFactorLinkResult.NotFound());
        }

        if (!account.IsTwoFactorEnabled)
        {
            return PreparedChanges<AdminTwoFactorLinkResult>.NothingToSave(new AdminTwoFactorLinkResult.TwoFactorNotEnabled());
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var lastPasswordChange = await securityEvents.FindLastSuccessAsync(SecurityEventType.PasswordChange, account.Id, cancellationToken);

        if (!TwoFactorAuthenticationRules.IsPasswordOldEnoughForSupportLink(lastPasswordChange, now))
        {
            return PreparedChanges<AdminTwoFactorLinkResult>.NothingToSave(new AdminTwoFactorLinkResult.PasswordChangedRecently());
        }

        accountRepository.PrepareUnchangedCheck(account);
        emailRequests.PrepareRequest(EmailRequest.ForTwoFactorDeactivationLink(account.Id, ipAddress, now));
        securityJournal.PrepareAdminAction(administratorId, account.Id, ipAddress);

        return PreparedChanges<AdminTwoFactorLinkResult>.SaveThenReturn(new AdminTwoFactorLinkResult.Sent());
    }
}
