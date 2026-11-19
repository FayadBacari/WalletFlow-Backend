using System.Net;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Cas d'utilisation : l'utilisateur qui a perdu son téléphone et ses codes de secours désactive sa double authentification avec
// le lien que le support lui a fait envoyer, et son mot de passe (GR Auth, SS §2). Le lien prouve la boîte mail, le mot de passe
// prouve le compte. Le lien ne marche que si le mot de passe n'a pas changé depuis au moins 7 jours : un voleur de la boîte mail
// qui vient de le réinitialiser ne peut pas s'en servir. Chaque essai du mot de passe est compté avant sa vérification, en
// réécrivant le lien avec sa version : même lancés en même temps, 5 essais au plus sont vérifiés, puis le lien est annulé.
// La désactivation ferme toutes les sessions du compte, et envoie l'alerte de sécurité (GR Notif).
public class TwoFactorSupportLinkService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly ITwoFactorAuthenticationRepository twoFactorRepository;
    private readonly IUserSessionRepository sessionRepository;
    private readonly ISecurityEventRepository securityEvents;
    private readonly IEmailRequestRepository emailRequests;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly IPasswordHasher passwordHasher;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly TimeProvider clock;

    // Reçoit les outils dont la désactivation a besoin.
    public TwoFactorSupportLinkService(
        IUserAccountRepository accountRepository,
        ITwoFactorAuthenticationRepository twoFactorRepository,
        IUserSessionRepository sessionRepository,
        ISecurityEventRepository securityEvents,
        IEmailRequestRepository emailRequests,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.twoFactorRepository = twoFactorRepository;
        this.sessionRepository = sessionRepository;
        this.securityEvents = securityEvents;
        this.emailRequests = emailRequests;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.passwordHasher = passwordHasher;
        this.tokenGenerator = tokenGenerator;
        this.clock = clock;
    }

    // Compte d'abord l'essai, puis vérifie le mot de passe lu avec le lien. S'il est bon, désactive la double authentification,
    // puis ferme toutes les sessions du compte.
    public async Task<TwoFactorSupportLinkResult> DeactivateAsync(string linkToken, string password, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenGenerator.HashToken(linkToken);
        // L'essai compte même si le client raccroche : il ne peut pas essayer plus de 5 mots de passe en abandonnant ses requêtes.
        var attempt = await savingService.SaveWithRetriesAsync(
            () => PrepareTryAsync(tokenHash, CancellationToken.None),
            PasswordTry.Refused(new TwoFactorSupportLinkResult.LinkNoLongerValid()),
            CancellationToken.None);

        if (attempt.Refusal != null)
        {
            return attempt.Refusal;
        }

        if (!await passwordHasher.VerifyAsync(PasswordRules.Normalize(password), attempt.PasswordHash, cancellationToken))
        {
            await securityJournal.SaveEventNowAsync(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Failure, attempt.AccountId, ipAddress);

            return attempt.WasLastTry ? new TwoFactorSupportLinkResult.LinkCancelled() : new TwoFactorSupportLinkResult.WrongPassword();
        }

        var deactivated = await savingService.SaveWithRetriesAsync(
            () => PrepareDeactivationAsync(tokenHash, attempt, ipAddress, cancellationToken),
            false,
            cancellationToken);

        if (!deactivated)
        {
            return new TwoFactorSupportLinkResult.LinkNoLongerValid();
        }

        // Après l'enregistrement, et pas pendant : une connexion en cours a soit déjà ouvert sa session, effacée ici, soit
        // trouvera la double authentification désactivée (SS §3).
        await sessionRepository.DeleteAllForUserAsync(attempt.AccountId, cancellationToken);
        return new TwoFactorSupportLinkResult.Deactivated();
    }

    // Un passage : lit le lien et son compte, vérifie le lien et la règle des 7 jours, puis compte l'essai. Le lien est réécrit
    // avec sa version : de deux essais simultanés, le second échoue, relit le lien, et compte après le premier.
    private async Task<PreparedChanges<PasswordTry>> PrepareTryAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var token = await accountRepository.FindTokenAsync(tokenHash, EmailTokenType.TwoFactorDeactivation, cancellationToken);

        if (token == null || !token.CanBeUsedAt(now) || !token.HasPasswordTriesLeft())
        {
            return PreparedChanges<PasswordTry>.NothingToSave(PasswordTry.Refused(new TwoFactorSupportLinkResult.LinkNoLongerValid()));
        }

        var account = await accountRepository.FindByIdAsync(token.UserId, cancellationToken);

        if (account == null || !account.IsTwoFactorEnabled)
        {
            return PreparedChanges<PasswordTry>.NothingToSave(PasswordTry.Refused(new TwoFactorSupportLinkResult.LinkNoLongerValid()));
        }

        if (!await IsPasswordOldEnoughAsync(account.Id, now, cancellationToken))
        {
            return PreparedChanges<PasswordTry>.NothingToSave(PasswordTry.Refused(new TwoFactorSupportLinkResult.PasswordChangedRecently()));
        }

        token.CountPasswordTry();

        return PreparedChanges<PasswordTry>.SaveThenReturn(new PasswordTry
        {
            AccountId = account.Id,
            PasswordHash = account.PasswordHash,
            WasLastTry = !token.HasPasswordTriesLeft()
        });
    }

    // Un passage : relit le lien et le compte, et vérifie que rien n'a changé depuis l'essai : le lien n'a pas servi ni expiré,
    // le mot de passe vérifié est toujours le bon, la double authentification est toujours activée, et le mot de passe a toujours
    // au moins 7 jours. Puis prépare la désactivation, les codes de secours effacés, le lien utilisé, l'événement du journal et
    // l'alerte. Le 5e essai peut réussir : le lien n'a alors plus d'essai, mais il n'a pas encore servi.
    private async Task<PreparedChanges<bool>> PrepareDeactivationAsync(string tokenHash, PasswordTry attempt, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var token = await accountRepository.FindTokenAsync(tokenHash, EmailTokenType.TwoFactorDeactivation, cancellationToken);
        var account = await accountRepository.FindByIdAsync(attempt.AccountId, cancellationToken);

        if (token == null
            || !token.CanBeUsedAt(now)
            || account == null
            || account.PasswordHash != attempt.PasswordHash
            || !account.IsTwoFactorEnabled
            || !await IsPasswordOldEnoughAsync(account.Id, now, cancellationToken))
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        account.DeactivateTwoFactor();
        await twoFactorRepository.PrepareBackupCodesRemovalAsync(account.Id, cancellationToken);
        token.MarkAsUsed(now);
        securityJournal.PrepareEvent(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Success, account.Id, ipAddress);
        emailRequests.PrepareRequest(EmailRequest.ForTwoFactorDeactivation(account.Id, ipAddress, now));

        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Renvoie vrai si le mot de passe du compte n'a pas changé depuis au moins 7 jours, d'après les changements réussis du journal.
    // Un changement et une réinitialisation écrivent tous les deux un changement de mot de passe réussi.
    private async Task<bool> IsPasswordOldEnoughAsync(Guid accountId, DateTime now, CancellationToken cancellationToken = default)
    {
        var lastPasswordChange = await securityEvents.FindLastSuccessAsync(SecurityEventType.PasswordChange, accountId, cancellationToken);

        return TwoFactorAuthenticationRules.IsPasswordOldEnoughForSupportLink(lastPasswordChange, now);
    }

    // Un essai compté : le compte du lien et le mot de passe à vérifier, ou la raison du refus.
    private sealed class PasswordTry
    {
        public TwoFactorSupportLinkResult? Refusal { get; init; }
        public Guid AccountId { get; init; }
        public string PasswordHash { get; init; } = string.Empty;

        // Vrai si cet essai était le 5e : le lien n'en a plus.
        public bool WasLastTry { get; init; }

        // Un essai qui n'est pas compté, pour cette raison.
        public static PasswordTry Refused(TwoFactorSupportLinkResult refusal)
        {
            return new PasswordTry { Refusal = refusal };
        }
    }
}
