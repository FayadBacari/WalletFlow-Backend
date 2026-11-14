using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountSettings;

// Cas d'utilisation : l'utilisateur confirme sa nouvelle adresse avec le lien reçu et son mot de passe (GR Compte, SS §2).
// Le lien prouve la boîte mail, le mot de passe prouve le compte : une faute de frappe dans l'adresse ne donne pas le compte à un inconnu.
// Tant que la nouvelle adresse n'est pas confirmée, l'ancienne reste valable.
public class EmailChangeConfirmationService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly IPasswordHasher passwordHasher;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly IBackgroundEmailRequestQueue emailQueue;
    private readonly TimeProvider clock;

    // Reçoit les outils dont la confirmation a besoin.
    public EmailChangeConfirmationService(
        IUserAccountRepository accountRepository,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        IBackgroundEmailRequestQueue emailQueue,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.passwordHasher = passwordHasher;
        this.tokenGenerator = tokenGenerator;
        this.emailQueue = emailQueue;
        this.clock = clock;
    }

    // Change l'adresse si le lien est encore valable et si le mot de passe est le bon. Un mauvais mot de passe ne consomme pas le lien.
    // Une fois le changement fait, l'ancienne adresse est prévenue, en arrière-plan (GR Compte).
    public async Task<EmailChangeConfirmationResult> ConfirmAsync(string linkToken, string password, IPAddress? ipAddress)
    {
        var tokenHash = tokenGenerator.HashToken(linkToken);

        var token = await FindUsableTokenAsync(tokenHash);
        if (token == null)
        {
            return new EmailChangeConfirmationResult.LinkNoLongerValid();
        }

        var account = await accountRepository.FindByIdAsync(token.UserId);
        if (account == null || !account.IsEmailConfirmed)
        {
            return new EmailChangeConfirmationResult.LinkNoLongerValid();
        }

        var checkedPasswordHash = account.PasswordHash;
        var normalizedPassword = PasswordRules.Normalize(password);

        if (!await passwordHasher.VerifyAsync(normalizedPassword, checkedPasswordHash))
        {
            await securityJournal.SaveEventNowAsync(SecurityEventType.EmailChange, SecurityEventResult.Failure, account.Id, ipAddress);
            return new EmailChangeConfirmationResult.WrongPassword();
        }

        var oldEmail = account.Email;
        var result = await savingService.SaveWithRetriesAsync(
            () => PrepareNewEmailAsync(tokenHash, checkedPasswordHash, ipAddress),
            new EmailChangeConfirmationResult.LinkNoLongerValid());

        if (result is EmailChangeConfirmationResult.EmailChanged)
        {
            emailQueue.Add(new BackgroundEmailRequest.EmailChangedNotice
            {
                AccountId = account.Id,
                OldEmailAddress = oldEmail,
                IpAddress = ipAddress
            });
        }

        return result;
    }

    // Un passage : relit le lien et son compte, les vérifie, puis prépare la nouvelle adresse, le lien utilisé, l'effacement
    // des liens de récupération et de déblocage partis vers l'ancienne adresse (SS §2), et l'événement du journal.
    private async Task<PreparedChanges<EmailChangeConfirmationResult>> PrepareNewEmailAsync(
        string tokenHash,
        string checkedPasswordHash,
        IPAddress? ipAddress)
    {
        var token = await FindUsableTokenAsync(tokenHash);
        if (token == null || token.NewEmail == null)
        {
            return PreparedChanges<EmailChangeConfirmationResult>.NothingToSave(new EmailChangeConfirmationResult.LinkNoLongerValid());
        }

        var account = await accountRepository.FindByIdAsync(token.UserId);
        if (account == null
            || !account.IsEmailConfirmed
            || account.PasswordHash != checkedPasswordHash)
        {
            return PreparedChanges<EmailChangeConfirmationResult>.NothingToSave(new EmailChangeConfirmationResult.LinkNoLongerValid());
        }

        // Le premier compte qui confirme une adresse la garde : un autre compte l'a peut-être prise depuis la demande.
        if (await accountRepository.FindByEmailAsync(token.NewEmail) != null)
        {
            return PreparedChanges<EmailChangeConfirmationResult>.NothingToSave(new EmailChangeConfirmationResult.EmailAlreadyUsed());
        }

        account.ReplaceEmail(token.NewEmail);
        token.MarkAsUsed(clock.GetUtcNow().UtcDateTime);
        await accountRepository.PrepareRecoveryAndUnlockLinksRemovalAsync(account.Id);
        securityJournal.PrepareEvent(SecurityEventType.EmailChange, SecurityEventResult.Success, account.Id, ipAddress);

        return PreparedChanges<EmailChangeConfirmationResult>.SaveThenReturn(new EmailChangeConfirmationResult.EmailChanged());
    }

    // Cherche le jeton du lien. Renvoie null s'il n'existe pas, s'il a déjà servi ou s'il a expiré.
    private async Task<EmailToken?> FindUsableTokenAsync(string tokenHash)
    {
        var token = await accountRepository.FindTokenAsync(tokenHash, EmailTokenType.EmailChange);

        if (token == null || !token.CanBeUsedAt(clock.GetUtcNow().UtcDateTime))
        {
            return null;
        }

        return token;
    }
}
