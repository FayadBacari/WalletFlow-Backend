using System.Security.Cryptography;
using System.Text;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Vérifie un code de double authentification : un code de l'application, ou un code de secours (GR Auth, SS §2).
public class TwoFactorCodeCheckingService
{
    private readonly ITwoFactorAuthenticationRepository twoFactorRepository;
    private readonly IBackupCodeHasher backupCodeHasher;
    private readonly IAuthenticatorCodeTool authenticatorCodeTool;
    private readonly ITotpSecretProtector secretProtector;
    private readonly TimeProvider clock;

    // Reçoit les outils dont la vérification a besoin.
    public TwoFactorCodeCheckingService(
        ITwoFactorAuthenticationRepository twoFactorRepository,
        IBackupCodeHasher backupCodeHasher,
        IAuthenticatorCodeTool authenticatorCodeTool,
        ITotpSecretProtector secretProtector,
        TimeProvider clock)
    {
        this.twoFactorRepository = twoFactorRepository;
        this.backupCodeHasher = backupCodeHasher;
        this.authenticatorCodeTool = authenticatorCodeTool;
        this.secretProtector = secretProtector;
        this.clock = clock;
    }

    // Vérifie un code pour un compte déjà activé. Un texte qui n'a la forme d'aucun code est refusé sans rien calculer.
    // Le secret n'est déchiffré que pour un code de l'application : si la clé est perdue, les codes de secours marchent encore.
    public async Task<TwoFactorCodeCheckResult> CheckAsync(User account, string typedCode, CancellationToken cancellationToken = default)
    {
        var cleanedCode = TwoFactorAuthenticationRules.CleanCode(typedCode);

        if (TwoFactorAuthenticationRules.HasAuthenticatorCodeFormat(cleanedCode))
        {
            // Un compte sans secret n'a aucun code de l'application : le code est refusé.
            if (account.EncryptedTotpSecret == null)
            {
                return new TwoFactorCodeCheckResult { IsCodeValid = false };
            }

            var secret = secretProtector.Decrypt(account.EncryptedTotpSecret, account.Id);
            var step = FindStep(secret, cleanedCode);

            // Un code déjà utilisé, ou plus ancien que le dernier utilisé, reçoit la même réponse qu'un mauvais code (SS §2).
            if (step == null || !account.CanUseAuthenticatorStep(step.Value))
            {
                return new TwoFactorCodeCheckResult { IsCodeValid = false };
            }

            return new TwoFactorCodeCheckResult { IsCodeValid = true, AuthenticatorStep = step };
        }

        if (TwoFactorAuthenticationRules.HasBackupCodeFormat(cleanedCode))
        {
            return await CheckBackupCodeAsync(account.Id, cleanedCode, cancellationToken);
        }

        return new TwoFactorCodeCheckResult { IsCodeValid = false };
    }

    // Vérifie le premier code, à l'activation : seul un code de l'application peut convenir, le compte n'a pas encore de code de
    // secours. Renvoie le pas de temps du code, que l'activation note comme utilisé, ou null si le code est faux.
    public long? CheckFirstCode(string secret, string typedCode)
    {
        var cleanedCode = TwoFactorAuthenticationRules.CleanCode(typedCode);

        if (!TwoFactorAuthenticationRules.HasAuthenticatorCodeFormat(cleanedCode))
        {
            return null;
        }

        return FindStep(secret, cleanedCode);
    }

    // Compare le code au secret, et renvoie son pas de temps s'il est valable maintenant.
    private long? FindStep(string secret, string cleanedCode)
    {
        return authenticatorCodeTool.FindValidPeriod(secret, cleanedCode, clock.GetUtcNow().UtcDateTime);
    }

    // Hache le code une seule fois, avec le sel du compte, puis compare cette empreinte à celle de chaque code encore inutilisé (SS §2).
    // Un mauvais code ne coûte donc qu'un seul calcul Argon2id, quel que soit le nombre de codes qui restent.
    private async Task<TwoFactorCodeCheckResult> CheckBackupCodeAsync(Guid accountId, string cleanedCode, CancellationToken cancellationToken = default)
    {
        var unusedCodes = await twoFactorRepository.FindUnusedBackupCodesAsync(accountId, cancellationToken);
        var codeHash = await backupCodeHasher.HashAsync(cleanedCode, accountId, cancellationToken);

        foreach (var backupCode in unusedCodes)
        {
            if (HashesAreEqual(codeHash, backupCode.CodeHash))
            {
                return new TwoFactorCodeCheckResult { IsCodeValid = true, BackupCodeId = backupCode.Id };
            }
        }

        return new TwoFactorCodeCheckResult { IsCodeValid = false };
    }

    // Compare deux empreintes en un temps qui ne dépend pas de leur contenu.
    private static bool HashesAreEqual(string firstHash, string secondHash)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(firstHash), Encoding.UTF8.GetBytes(secondHash));
    }
}
