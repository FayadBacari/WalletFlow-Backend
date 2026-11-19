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
    public async Task<AccountIdentityCheckResult> CheckAsync(User account, string password, string? code, CancellationToken cancellationToken = default)
    {
        var normalizedPassword = PasswordRules.Normalize(password);

        if (!await passwordHasher.VerifyAsync(normalizedPassword, account.PasswordHash, cancellationToken))
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

        var codeCheck = await codeCheckingService.CheckAsync(account, code, cancellationToken);

        return new AccountIdentityCheckResult
        {
            IsConfirmed = codeCheck.IsCodeValid,
            BackupCodeId = codeCheck.BackupCodeId,
            AuthenticatorStep = codeCheck.AuthenticatorStep
        };
    }

    // Prépare l'usage du code reconnu, s'il y en a un, sur le compte relu par le passage : le pas de temps d'un code de
    // l'application, ou le code de secours avec l'alerte qui l'annonce (GR Notif). Renvoie faux si le code ne peut plus servir :
    // un code de l'application déjà utilisé entre-temps, un code de secours effacé ou déjà utilisé. Rien n'est alors préparé.
    // L'enregistrement refusera tout si une autre requête se sert du même code au même instant : le compte et le code de
    // secours sont contrôlés à l'écriture.
    public async Task<bool> TryPrepareCodeUseAsync(
        User account,
        Guid? backupCodeId,
        long? authenticatorStep,
        IPAddress? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (authenticatorStep is long step && !account.CanUseAuthenticatorStep(step))
        {
            return false;
        }

        BackupCode? backupCode = null;
        if (backupCodeId != null)
        {
            backupCode = await twoFactorRepository.FindBackupCodeAsync(backupCodeId.Value, cancellationToken);
            if (backupCode == null || backupCode.UsedAt != null || backupCode.UserId != account.Id)
            {
                return false;
            }
        }

        if (authenticatorStep is long usedStep)
        {
            account.UseAuthenticatorStep(usedStep);
        }

        if (backupCode != null)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            backupCode.MarkAsUsed(now);
            emailRequests.PrepareRequest(EmailRequest.ForBackupCodeUse(backupCode.UserId, ipAddress, now));
        }

        return true;
    }
}
