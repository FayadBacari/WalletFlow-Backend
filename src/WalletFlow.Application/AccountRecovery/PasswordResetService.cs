using System.Net;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountRecovery;

// Cas d'utilisation : un visiteur choisit un nouveau mot de passe avec le lien reçu par e-mail (GR Auth, SS §2 et §3).
// Le lien a prouvé qu'il possède la boîte mail : le compte est aussi débloqué. Il ne connecte personne.
public class PasswordResetService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IUserSessionRepository sessionRepository;
    private readonly IEmailRequestRepository emailRequests;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly IPasswordHasher passwordHasher;
    private readonly IBreachedPasswordChecker breachedPasswordChecker;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly TimeProvider clock;

    // Reçoit les outils dont le changement de mot de passe a besoin.
    public PasswordResetService(
        IUserAccountRepository accountRepository,
        IUserSessionRepository sessionRepository,
        IEmailRequestRepository emailRequests,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
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
        this.passwordHasher = passwordHasher;
        this.breachedPasswordChecker = breachedPasswordChecker;
        this.tokenGenerator = tokenGenerator;
        this.clock = clock;
    }

    // Change le mot de passe si le lien est encore valable. Un mot de passe refusé par les règles, ou trouvé dans une fuite
    // connue, ne consomme pas le lien.
    public async Task<PasswordResetResult> ResetAsync(string linkToken, string newPassword, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        var tokenHash = tokenGenerator.HashToken(linkToken);

        if (await FindUsableTokenAsync(tokenHash, cancellationToken) == null)
        {
            return new PasswordResetResult.LinkNoLongerValid();
        }

        var normalizedPassword = PasswordRules.Normalize(newPassword);

        if (!PasswordRules.IsValid(normalizedPassword))
        {
            return new PasswordResetResult.PasswordBreaksRules();
        }

        if (await breachedPasswordChecker.IsBreachedAsync(normalizedPassword, cancellationToken))
        {
            return new PasswordResetResult.PasswordBreached();
        }

        // Le hachage est lent : il passe avant la lecture du compte, pour que l'écriture suive de près la lecture.
        var newPasswordHash = await passwordHasher.HashAsync(normalizedPassword, cancellationToken);

        return await SavePasswordThenCloseSessionsAsync(tokenHash, newPasswordHash, ipAddress, cancellationToken);
    }

    // Enregistre le changement, puis ferme toutes les sessions du compte. Si une autre requête a changé le compte ou le lien,
    // tout est relu : un lien déjà utilisé ou remplacé entre-temps est alors refusé.
    private async Task<PasswordResetResult> SavePasswordThenCloseSessionsAsync(string tokenHash, string newPasswordHash, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        var accountId = await savingService.SaveWithRetriesAsync<Guid?>(
            () => PrepareNewPasswordAsync(tokenHash, newPasswordHash, ipAddress, cancellationToken),
            null,
            cancellationToken);

        if (accountId == null)
        {
            return new PasswordResetResult.LinkNoLongerValid();
        }

        // Après l'enregistrement, et pas pendant : une connexion en cours avec l'ancien mot de passe a soit
        // déjà ouvert sa session, qui est effacée ici, soit trouvera le nouveau mot de passe et sera refusée (SS §3).
        await sessionRepository.DeleteAllForUserAsync(accountId.Value, cancellationToken);
        return new PasswordResetResult.PasswordChanged();
    }

    // Un passage : lit le lien et son compte, les vérifie, puis prépare le nouveau mot de passe, le compte débloqué,
    // le lien utilisé, l'événement du journal et l'alerte du changement (GR Notif).
    // Donne l'identifiant du compte, ou null si le lien ne vaut plus.
    private async Task<PreparedChanges<Guid?>> PrepareNewPasswordAsync(string tokenHash, string newPasswordHash, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        var token = await FindUsableTokenAsync(tokenHash, cancellationToken);
        if (token == null)
        {
            return PreparedChanges<Guid?>.NothingToSave(null);
        }

        var account = await accountRepository.FindByIdAsync(token.UserId, cancellationToken);
        if (account == null || !account.IsEmailConfirmed)
        {
            return PreparedChanges<Guid?>.NothingToSave(null);
        }

        var now = clock.GetUtcNow().UtcDateTime;

        account.ReplacePasswordHash(newPasswordHash);
        account.Unlock();
        token.MarkAsUsed(now);
        securityJournal.PrepareEvent(SecurityEventType.PasswordChange, SecurityEventResult.Success, account.Id, ipAddress);
        emailRequests.PrepareRequest(EmailRequest.ForPasswordChange(account.Id, ipAddress, now));

        return PreparedChanges<Guid?>.SaveThenReturn(account.Id);
    }

    // Cherche le jeton du lien. Renvoie null s'il n'existe pas, s'il a déjà servi ou s'il a expiré.
    private async Task<EmailToken?> FindUsableTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var token = await accountRepository.FindTokenAsync(tokenHash, EmailTokenType.PasswordRecovery, cancellationToken);

        if (token == null
            || !token.CanBeUsedAt(clock.GetUtcNow().UtcDateTime))
        {
            return null;
        }

        return token;
    }
}
