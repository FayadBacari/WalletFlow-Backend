using System.Net;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Cas d'utilisation : un utilisateur connecté active la double authentification, en deux temps (GR Auth, SS §2).
// Le mot de passe donne le secret, puis un premier code active tout : un code QR mal scanné n'enferme donc personne dehors.
public class TwoFactorActivationService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly ITwoFactorAuthenticationRepository twoFactorRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly IPasswordHasher passwordHasher;
    private readonly IBackupCodeHasher backupCodeHasher;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly IAuthenticatorCodeTool authenticatorCodeTool;
    private readonly ITotpSecretProtector secretProtector;
    private readonly IWaitingSecretStore waitingSecretStore;
    private readonly TwoFactorCodeCheckingService codeCheckingService;

    // Reçoit les outils dont l'activation a besoin.
    public TwoFactorActivationService(
        IUserAccountRepository accountRepository,
        ITwoFactorAuthenticationRepository twoFactorRepository,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        IPasswordHasher passwordHasher,
        IBackupCodeHasher backupCodeHasher,
        ISecureTokenGenerator tokenGenerator,
        IAuthenticatorCodeTool authenticatorCodeTool,
        ITotpSecretProtector secretProtector,
        IWaitingSecretStore waitingSecretStore,
        TwoFactorCodeCheckingService codeCheckingService)
    {
        this.accountRepository = accountRepository;
        this.twoFactorRepository = twoFactorRepository;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.passwordHasher = passwordHasher;
        this.backupCodeHasher = backupCodeHasher;
        this.tokenGenerator = tokenGenerator;
        this.authenticatorCodeTool = authenticatorCodeTool;
        this.secretProtector = secretProtector;
        this.waitingSecretStore = waitingSecretStore;
        this.codeCheckingService = codeCheckingService;
    }

    // Premier temps : le mot de passe redemandé donne le secret. Une session volée ne peut donc pas enfermer le propriétaire dehors (SS §2).
    // Rien n'est écrit en base : le secret attend le premier code, en mémoire.
    public async Task<TwoFactorActivationStartResult> StartAsync(Guid userId, string password, IPAddress? ipAddress)
    {
        var account = await accountRepository.FindByIdAsync(userId);

        if (account == null)
        {
            return new TwoFactorActivationStartResult.WrongPassword();
        }

        if (account.IsTwoFactorEnabled)
        {
            return new TwoFactorActivationStartResult.AlreadyEnabled();
        }

        var normalizedPassword = PasswordRules.Normalize(password);

        if (!await passwordHasher.VerifyAsync(normalizedPassword, account.PasswordHash))
        {
            await securityJournal.SaveEventNowAsync(SecurityEventType.TwoFactorActivation, SecurityEventResult.Failure, userId, ipAddress);
            return new TwoFactorActivationStartResult.WrongPassword();
        }

        var secret = authenticatorCodeTool.GenerateSecret();
        waitingSecretStore.Save(userId, secret);

        return new TwoFactorActivationStartResult.Started
        {
            Secret = secret,
            SetupLink = authenticatorCodeTool.BuildSetupLink(secret, account.Email)
        };
    }

    // Second temps : un premier code juste active tout, et les 10 codes de secours sont donnés une seule fois (GR Auth).
    public async Task<TwoFactorActivationFinishResult> FinishAsync(Guid userId, string code, IPAddress? ipAddress)
    {
        var waitingSecret = waitingSecretStore.Find(userId);

        if (waitingSecret == null)
        {
            return new TwoFactorActivationFinishResult.ActivationMustRestart();
        }

        if (!codeCheckingService.CheckFirstCode(userId, waitingSecret, code))
        {
            await securityJournal.SaveEventNowAsync(SecurityEventType.TwoFactorActivation, SecurityEventResult.Failure, userId, ipAddress);
            return new TwoFactorActivationFinishResult.WrongCode();
        }

        return await SaveThenGiveBackupCodesAsync(userId, waitingSecret, ipAddress);
    }

    // Prépare les codes de secours et le secret chiffré, enregistre le tout, puis oublie le secret en mémoire.
    // Le hachage est lent : il passe avant la lecture du compte, pour que l'écriture suive de près la lecture.
    private async Task<TwoFactorActivationFinishResult> SaveThenGiveBackupCodesAsync(Guid userId, string waitingSecret, IPAddress? ipAddress)
    {
        var cleanedCodes = GenerateBackupCodes();
        var backupCodes = await HashBackupCodesAsync(cleanedCodes, userId);
        var encryptedSecret = secretProtector.Encrypt(waitingSecret, userId);

        var saved = await TrySaveActivationAsync(
            userId,
            encryptedSecret,
            backupCodes,
            ipAddress);

        waitingSecretStore.Remove(userId);

        if (!saved)
        {
            return new TwoFactorActivationFinishResult.ActivationMustRestart();
        }

        return new TwoFactorActivationFinishResult.Activated { BackupCodes = FormatBackupCodes(cleanedCodes) };
    }

    // Enregistre l'activation. Si une autre requête a changé le compte, le compte est relu et tout recommence.
    // Renvoie faux si le compte a disparu, s'il vient d'être activé par une autre requête, ou s'il change encore au bout de 10 passages.
    private async Task<bool> TrySaveActivationAsync(
        Guid userId,
        string encryptedSecret,
        List<BackupCode> backupCodes,
        IPAddress? ipAddress)
    {
        return await savingService.SaveWithRetriesAsync(
            () => PrepareActivationAsync(userId, encryptedSecret, backupCodes, ipAddress),
            false);
    }

    // Un passage : lit le compte, refuse s'il a disparu ou s'il est déjà activé : un compte ne reçoit jamais 20 codes de secours.
    // Sinon, prépare le secret chiffré, les codes de secours et l'événement du journal.
    private async Task<PreparedChanges<bool>> PrepareActivationAsync(
        Guid userId,
        string encryptedSecret,
        List<BackupCode> backupCodes,
        IPAddress? ipAddress)
    {
        var account = await accountRepository.FindByIdAsync(userId);

        if (account == null || account.IsTwoFactorEnabled)
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        account.ActivateTwoFactor(encryptedSecret);
        twoFactorRepository.PrepareBackupCodes(backupCodes);
        securityJournal.PrepareEvent(SecurityEventType.TwoFactorActivation, SecurityEventResult.Success, userId, ipAddress);

        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Tire au hasard les 10 codes de secours (GR Auth).
    private List<string> GenerateBackupCodes()
    {
        var cleanedCodes = new List<string>();

        for (var codeNumber = 1; codeNumber <= TwoFactorAuthenticationRules.BackupCodesPerActivation; codeNumber++)
        {
            cleanedCodes.Add(tokenGenerator.GenerateBackupCode());
        }

        return cleanedCodes;
    }

    // Hache chaque code de secours comme un mot de passe, avec le sel du compte : seule l'empreinte va en base (SS §2).
    private async Task<List<BackupCode>> HashBackupCodesAsync(List<string> cleanedCodes, Guid userId)
    {
        var backupCodes = new List<BackupCode>();

        foreach (var cleanedCode in cleanedCodes)
        {
            backupCodes.Add(new BackupCode
            {
                CodeHash = await backupCodeHasher.HashAsync(cleanedCode, userId),
                UserId = userId
            });
        }

        return backupCodes;
    }

    // Écrit les codes de secours comme ils sont remis à l'utilisateur.
    private static List<string> FormatBackupCodes(List<string> cleanedCodes)
    {
        var displayedCodes = new List<string>();

        foreach (var cleanedCode in cleanedCodes)
        {
            displayedCodes.Add(TwoFactorAuthenticationRules.FormatBackupCode(cleanedCode));
        }

        return displayedCodes;
    }
}
