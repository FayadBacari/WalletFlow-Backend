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
    private const int NumberOfRandomBytesInSecureToken = 32;

    // Tire au hasard un nouveau jeton, lisible dans une adresse web, et calcule son empreinte SHA-256.
    public GeneratedSecureToken GenerateNewRandomSecureTokenWithItsHash()
    {
        var randomBytesOfTheNewSecureToken = RandomNumberGenerator.GetBytes(NumberOfRandomBytesInSecureToken);
        var tokenGivenOnlyToTheVisitor = Base64Url.EncodeToString(randomBytesOfTheNewSecureToken);

        return new GeneratedSecureToken(tokenGivenOnlyToTheVisitor, ConvertSecureTokenIntoItsHash(tokenGivenOnlyToTheVisitor));
    }

    // Calcule l'empreinte SHA-256 d'un jeton, telle qu'elle est enregistrée en base.
    public string ConvertSecureTokenIntoItsHash(string tokenToConvertIntoItsHash)
    {
        var hashBytesOfTheSecureToken = SHA256.HashData(Encoding.UTF8.GetBytes(tokenToConvertIntoItsHash));

        return Convert.ToHexStringLower(hashBytesOfTheSecureToken);
    }

    // Tire au hasard un nouveau code de secours : GetString donne à chaque caractère permis la même chance d'être tiré.
    public string GenerateNewRandomBackupCodeInItsCleanForm()
    {
        return RandomNumberGenerator.GetString(TwoFactorAuthenticationRules.CharactersAllowedInOneBackupCode, TwoFactorAuthenticationRules.NumberOfCharactersInOneBackupCode);
    }
}
