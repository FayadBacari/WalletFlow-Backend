using System.Text;
using Sodium;
using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Infrastructure.Security;

// Hache les codes de secours en Argon2id avec libsodium, au même niveau que les mots de passe (SS §2).
// Le sel est l'identifiant du compte (16 octets, unique) : les 10 codes d'un compte ont donc le même sel, et un code tapé
// ne demande qu'un seul calcul. Ce sel commun n'aide pas une attaque : chaque code est tiré au hasard sur 50 bits
// (10 caractères parmi 32), bien trop pour être retrouvé à partir de son empreinte.
public class LibsodiumBackupCodeHasher : IBackupCodeHasher
{
    // Le début de chaque empreinte dit comment elle a été calculée.
    public const string HashPrefix = "argon2id-sel-du-compte$";

    private const long HashLength = 32;

    private readonly ArgonComputationsLimiter argonLimiter;

    // Reçoit le limiteur partagé par tous les calculs Argon2id du serveur.
    public LibsodiumBackupCodeHasher(ArgonComputationsLimiter argonLimiter)
    {
        this.argonLimiter = argonLimiter;
    }

    // Calcule l'empreinte du code avec le sel du compte. Le même code, pour le même compte, donne toujours la même empreinte.
    public async Task<string> HashAsync(string cleanedCode, Guid accountId)
    {
        var hashBytes = await argonLimiter.RunAsync(() =>
            PasswordHash.ArgonHashBinary(
                Encoding.UTF8.GetBytes(cleanedCode),
                accountId.ToByteArray(),
                PasswordHash.StrengthArgon.Interactive,
                HashLength,
                PasswordHash.ArgonAlgorithm.Argon_2ID13));

        return HashPrefix + Convert.ToBase64String(hashBytes);
    }
}
