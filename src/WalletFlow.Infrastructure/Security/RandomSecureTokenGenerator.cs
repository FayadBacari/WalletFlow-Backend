using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Security;

// Crée les jetons des liens envoyés par e-mail, des sessions et des appareils, avec le générateur cryptographique de .NET (SS §2 et §3).
public class RandomSecureTokenGenerator : ISecureTokenGenerator
{
    // 32 octets, soit 256 bits aléatoires : au-delà des 128 bits demandés par la SS §2.
    private const int TokenByteCount = 32;

    // Tire au hasard un nouveau jeton, lisible dans une adresse web, et calcule son empreinte SHA-256.
    public GeneratedSecureToken GenerateToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(TokenByteCount);
        var token = Base64Url.EncodeToString(randomBytes);

        return new GeneratedSecureToken(token, HashToken(token));
    }

    // Calcule l'empreinte SHA-256 d'un jeton, telle qu'elle est enregistrée en base.
    public string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return Convert.ToHexStringLower(hashBytes);
    }

    // Tire au hasard un nouveau code de secours : GetString donne à chaque caractère permis la même chance d'être tiré.
    public string GenerateBackupCode()
    {
        return RandomNumberGenerator.GetString(TwoFactorAuthenticationRules.BackupCodeAlphabet, TwoFactorAuthenticationRules.BackupCodeLength);
    }
}
