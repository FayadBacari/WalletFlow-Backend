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
    private readonly IUsedAuthenticatorCodesRegister usedCodesRegister;
    private readonly TimeProvider clock;

    // Reçoit les outils dont la vérification a besoin.
    public TwoFactorCodeCheckingService(
        ITwoFactorAuthenticationRepository twoFactorRepository,
        IBackupCodeHasher backupCodeHasher,
        IAuthenticatorCodeTool authenticatorCodeTool,
        ITotpSecretProtector secretProtector,
        IUsedAuthenticatorCodesRegister usedCodesRegister,
        TimeProvider clock)
    {
        this.twoFactorRepository = twoFactorRepository;
        this.backupCodeHasher = backupCodeHasher;
        this.authenticatorCodeTool = authenticatorCodeTool;
        this.secretProtector = secretProtector;
        this.usedCodesRegister = usedCodesRegister;
        this.clock = clock;
    }

    // Vérifie un code pour un compte déjà activé. Un texte qui n'a la forme d'aucun code est refusé sans rien calculer.
    // Le secret n'est déchiffré que pour un code de l'application : si la clé est perdue, les codes de secours marchent encore.
    public async Task<TwoFactorCodeCheckResult> CheckAsync(User account, string typedCode)
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

            return new TwoFactorCodeCheckResult
            {
                IsCodeValid = CheckAuthenticatorCode(account.Id, secret, cleanedCode)
            };
        }

        if (TwoFactorAuthenticationRules.HasBackupCodeFormat(cleanedCode))
        {
            return await CheckBackupCodeAsync(account.Id, cleanedCode);
        }

        return new TwoFactorCodeCheckResult { IsCodeValid = false };
    }

    // Vérifie le premier code, à l'activation : seul un code de l'application peut convenir, le compte n'a pas encore de code de secours.
    public bool CheckFirstCode(Guid accountId, string secret, string typedCode)
    {
        var cleanedCode = TwoFactorAuthenticationRules.CleanCode(typedCode);

        return CheckAuthenticatorCode(accountId, secret, cleanedCode);
    }

    // Compare le code au secret, puis le note comme utilisé : un code déjà utilisé reçoit la même réponse qu'un mauvais code (SS §2).
    private bool CheckAuthenticatorCode(Guid accountId, string secret, string cleanedCode)
    {
        var period = authenticatorCodeTool.FindValidPeriod(
            secret,
            cleanedCode,
            clock.GetUtcNow().UtcDateTime);

        if (period == null)
        {
            return false;
        }

        return usedCodesRegister.TryRecordUse(accountId, period.Value);
    }

    // Hache le code une seule fois, avec le sel du compte, puis compare cette empreinte à celle de chaque code encore inutilisé (SS §2).
    // Un mauvais code ne coûte donc qu'un seul calcul Argon2id, quel que soit le nombre de codes qui restent.
    private async Task<TwoFactorCodeCheckResult> CheckBackupCodeAsync(Guid accountId, string cleanedCode)
    {
        var unusedCodes = await twoFactorRepository.FindUnusedBackupCodesAsync(accountId);
        var codeHash = await backupCodeHasher.HashAsync(cleanedCode, accountId);

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
