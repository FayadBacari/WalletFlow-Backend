using System.Net;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountSettings;

// Vérifie que la personne connectée est bien le propriétaire du compte, avant une action sensible (GR Compte, SS §2) :
// le mot de passe actuel, puis un code si la double authentification est activée. Une session volée ne suffit donc pas.
public class AccountIdentityCheckingService
{
    private readonly ITwoFactorAuthenticationRepository twoFactorRepository;
    private readonly IPasswordHasher passwordHasher;
    private readonly TwoFactorCodeCheckingService codeCheckingService;
    private readonly IEmailRequestRepository emailRequests;
    private readonly TimeProvider clock;

    // Reçoit les outils dont la vérification a besoin.
    public AccountIdentityCheckingService(
        ITwoFactorAuthenticationRepository twoFactorRepository,
        IPasswordHasher passwordHasher,
        TwoFactorCodeCheckingService codeCheckingService,
        IEmailRequestRepository emailRequests,
        TimeProvider clock)
    {
        this.twoFactorRepository = twoFactorRepository;
        this.passwordHasher = passwordHasher;
        this.codeCheckingService = codeCheckingService;
        this.emailRequests = emailRequests;
        this.clock = clock;
    }

    // Le mot de passe passe d'abord : un mauvais mot de passe ne consomme donc pas le code.
    // Les mauvais mots de passe saisis ici ne comptent pas pour le blocage du compte (SS §2).
    public async Task<AccountIdentityCheckResult> CheckAsync(User account, string password, string? code)
    {
        var normalizedPassword = PasswordRules.Normalize(password);

        if (!await passwordHasher.VerifyAsync(normalizedPassword, account.PasswordHash))
        {
            return new AccountIdentityCheckResult { IsConfirmed = false };
        }

        if (!account.IsTwoFactorEnabled)
        {
            return new AccountIdentityCheckResult { IsConfirmed = true };
        }

        if (string.IsNullOrEmpty(code))
        {
            return new AccountIdentityCheckResult { IsConfirmed = false };
        }

        var codeCheck = await codeCheckingService.CheckAsync(account, code);

        return new AccountIdentityCheckResult
        {
            IsConfirmed = codeCheck.IsCodeValid,
            BackupCodeId = codeCheck.BackupCodeId
        };
    }

    // Prépare l'usage du code de secours reconnu, s'il y en a un, avec l'alerte qui l'annonce (GR Notif). Renvoie faux s'il
    // n'existe plus ou s'il a déjà servi : rien n'est alors préparé. L'enregistrement refusera tout si une autre requête
    // s'en sert au même instant.
    public async Task<bool> TryPrepareBackupCodeUseAsync(Guid? backupCodeId, IPAddress? ipAddress)
    {
        if (backupCodeId == null)
        {
            return true;
        }

        var backupCode = await twoFactorRepository.FindBackupCodeAsync(backupCodeId.Value);
        if (backupCode == null || backupCode.UsedAt != null)
        {
            return false;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        backupCode.MarkAsUsed(now);
        emailRequests.PrepareRequest(EmailRequest.ForBackupCodeUse(backupCode.UserId, ipAddress, now));
        return true;
    }
}
