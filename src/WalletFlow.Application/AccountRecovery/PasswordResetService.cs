using System.Net;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountRecovery;

// Cas d'utilisation : un visiteur choisit un nouveau mot de passe avec le lien reçu par e-mail (GR Auth, SS §2 et §3).
// Le lien a prouvé qu'il possède la boîte mail : le compte est aussi débloqué. Il ne connecte personne.
public class PasswordResetService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IUserSessionRepository sessionRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly IPasswordHasher passwordHasher;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly TimeProvider clock;

    // Reçoit les outils dont le changement de mot de passe a besoin.
    public PasswordResetService(
        IUserAccountRepository accountRepository,
        IUserSessionRepository sessionRepository,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.sessionRepository = sessionRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.passwordHasher = passwordHasher;
        this.tokenGenerator = tokenGenerator;
        this.clock = clock;
    }

    // Change le mot de passe si le lien est encore valable. Un mot de passe refusé par les règles ne consomme pas le lien.
    public async Task<PasswordResetResult> ResetAsync(string linkToken, string newPassword, IPAddress? ipAddress)
    {
        var tokenHash = tokenGenerator.HashToken(linkToken);

        if (await FindUsableTokenAsync(tokenHash) == null)
        {
            return new PasswordResetResult.LinkNoLongerValid();
        }

        var normalizedPassword = PasswordRules.Normalize(newPassword);

        if (!PasswordRules.IsValid(normalizedPassword))
        {
            return new PasswordResetResult.PasswordBreaksRules();
        }

        // Le hachage est lent : il passe avant la lecture du compte, pour que l'écriture suive de près la lecture.
        var newPasswordHash = await passwordHasher.HashAsync(normalizedPassword);

        return await SavePasswordThenCloseSessionsAsync(tokenHash, newPasswordHash, ipAddress);
    }

    // Enregistre le changement, puis ferme toutes les sessions du compte. Si une autre requête a changé le compte ou le lien,
    // tout est relu : un lien déjà utilisé ou remplacé entre-temps est alors refusé.
    private async Task<PasswordResetResult> SavePasswordThenCloseSessionsAsync(string tokenHash, string newPasswordHash, IPAddress? ipAddress)
    {
        var accountId = await savingService.SaveWithRetriesAsync<Guid?>(
            () => PrepareNewPasswordAsync(tokenHash, newPasswordHash, ipAddress),
            null);

        if (accountId == null)
        {
            return new PasswordResetResult.LinkNoLongerValid();
        }

        // Après l'enregistrement, et pas pendant : une connexion en cours avec l'ancien mot de passe a soit
        // déjà ouvert sa session, qui est effacée ici, soit trouvera le nouveau mot de passe et sera refusée (SS §3).
        await sessionRepository.DeleteAllForUserAsync(accountId.Value);
        return new PasswordResetResult.PasswordChanged();
    }

    // Un passage : lit le lien et son compte, les vérifie, puis prépare le nouveau mot de passe, le compte débloqué,
    // le lien utilisé et l'événement du journal. Donne l'identifiant du compte, ou null si le lien ne vaut plus.
    private async Task<PreparedChanges<Guid?>> PrepareNewPasswordAsync(string tokenHash, string newPasswordHash, IPAddress? ipAddress)
    {
        var token = await FindUsableTokenAsync(tokenHash);
        if (token == null)
        {
            return PreparedChanges<Guid?>.NothingToSave(null);
        }

        var account = await accountRepository.FindByIdAsync(token.UserId);
        if (account == null || !account.IsEmailConfirmed)
        {
            return PreparedChanges<Guid?>.NothingToSave(null);
        }

        account.ReplacePasswordHash(newPasswordHash);
        account.Unlock();
        token.MarkAsUsed(clock.GetUtcNow().UtcDateTime);
        securityJournal.PrepareEvent(SecurityEventType.PasswordChange, SecurityEventResult.Success, account.Id, ipAddress);

        return PreparedChanges<Guid?>.SaveThenReturn(account.Id);
    }

    // Cherche le jeton du lien. Renvoie null s'il n'existe pas, s'il a déjà servi ou s'il a expiré.
    private async Task<EmailToken?> FindUsableTokenAsync(string tokenHash)
    {
        var token = await accountRepository.FindTokenAsync(tokenHash, EmailTokenType.PasswordRecovery);

        if (token == null
            || !token.CanBeUsedAt(clock.GetUtcNow().UtcDateTime))
        {
            return null;
        }

        return token;
    }
}
