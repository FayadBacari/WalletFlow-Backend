using System.Net;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Cas d'utilisation : un utilisateur connecté désactive la double authentification avec son mot de passe et un code (GR Auth, SS §2).
// Le code peut être un code de secours : c'est le seul moyen de changer de téléphone après en avoir perdu un.
public class TwoFactorDeactivationService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly ITwoFactorAuthenticationRepository twoFactorRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly IPasswordHasher passwordHasher;
    private readonly TwoFactorCodeCheckingService codeCheckingService;

    // Reçoit les outils dont la désactivation a besoin.
    public TwoFactorDeactivationService(
        IUserAccountRepository accountRepository,
        ITwoFactorAuthenticationRepository twoFactorRepository,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        IPasswordHasher passwordHasher,
        TwoFactorCodeCheckingService codeCheckingService)
    {
        this.accountRepository = accountRepository;
        this.twoFactorRepository = twoFactorRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.passwordHasher = passwordHasher;
        this.codeCheckingService = codeCheckingService;
    }

    // Vérifie le mot de passe puis le code : un mauvais mot de passe ne consomme donc pas le code. Si tout est bon, désactive.
    public async Task<TwoFactorDeactivationResult> DeactivateAsync(
        Guid userId,
        string password,
        string code,
        IPAddress? ipAddress)
    {
        var account = await accountRepository.FindByIdAsync(userId);

        if (account == null || !account.IsTwoFactorEnabled)
        {
            return TwoFactorDeactivationResult.TwoFactorNotEnabled;
        }

        var passwordHash = account.PasswordHash;
        var encryptedTotpSecret = account.EncryptedTotpSecret;
        var normalizedPassword = PasswordRules.Normalize(password);

        if (!await passwordHasher.VerifyAsync(normalizedPassword, passwordHash))
        {
            return await RefuseAsync(userId, ipAddress);
        }

        var codeCheck = await codeCheckingService.CheckAsync(account, code);

        if (!codeCheck.IsCodeValid)
        {
            return await RefuseAsync(userId, ipAddress);
        }

        return await SaveDeactivationAsync(userId, passwordHash, encryptedTotpSecret, ipAddress);
    }

    // Enregistre la désactivation. Si une autre requête a changé le compte, le compte est relu et tout recommence.
    // Si le compte change encore au bout de 10 passages, la désactivation est refusée.
    private async Task<TwoFactorDeactivationResult> SaveDeactivationAsync(
        Guid userId,
        string passwordHash,
        string? encryptedTotpSecret,
        IPAddress? ipAddress)
    {
        var result = await savingService.SaveWithRetriesAsync<TwoFactorDeactivationResult?>(
            () => PrepareDeactivationAsync(userId, passwordHash, encryptedTotpSecret, ipAddress),
            null);

        if (result == null)
        {
            return await RefuseAsync(userId, ipAddress);
        }

        return result.Value;
    }

    // Un passage : lit le compte et vérifie que le mot de passe et le secret qui viennent d'être contrôlés sont encore les bons,
    // car une récupération a pu passer entre-temps. Puis prépare le secret oublié, les codes de secours effacés et l'événement du journal.
    private async Task<PreparedChanges<TwoFactorDeactivationResult?>> PrepareDeactivationAsync(
        Guid userId,
        string passwordHash,
        string? encryptedTotpSecret,
        IPAddress? ipAddress)
    {
        var account = await accountRepository.FindByIdAsync(userId);

        if (account == null || !account.IsTwoFactorEnabled)
        {
            return PreparedChanges<TwoFactorDeactivationResult?>.NothingToSave(TwoFactorDeactivationResult.TwoFactorNotEnabled);
        }

        if (account.PasswordHash != passwordHash
            || account.EncryptedTotpSecret != encryptedTotpSecret)
        {
            return PreparedChanges<TwoFactorDeactivationResult?>.NothingToSave(await RefuseAsync(userId, ipAddress));
        }

        account.DeactivateTwoFactor();
        await twoFactorRepository.PrepareBackupCodesRemovalAsync(userId);
        securityJournal.PrepareEvent(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Success, userId, ipAddress);

        return PreparedChanges<TwoFactorDeactivationResult?>.SaveThenReturn(TwoFactorDeactivationResult.TwoFactorDeactivated);
    }

    // Note le refus dans le journal, et donne le même refus pour un mauvais mot de passe et pour un mauvais code.
    private async Task<TwoFactorDeactivationResult> RefuseAsync(Guid userId, IPAddress? ipAddress)
    {
        await securityJournal.SaveEventNowAsync(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Failure, userId, ipAddress);

        return TwoFactorDeactivationResult.WrongPasswordOrCode;
    }
}
