using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using WalletFlow.Application.Security;

namespace WalletFlow.Infrastructure.Security;

// Crée les jetons des liens envoyés par e-mail, avec le générateur cryptographique de .NET (SS §2).
public class RandomSecureTokenGenerator : ISecureTokenGenerator
{
    // 32 octets, soit 256 bits aléatoires : au-delà des 128 bits demandés par la SS §2.
    private const int NumberOfRandomBytesInSecureToken = 32;

    // Tire au hasard un nouveau jeton, lisible dans une adresse web, et calcule son empreinte SHA-256.
    public GeneratedSecureToken GenerateNewRandomSecureTokenWithItsHash()
    {
        var randomBytesOfTheNewSecureToken = RandomNumberGenerator.GetBytes(NumberOfRandomBytesInSecureToken);
        var tokenToSendInTheEmailLink = Base64Url.EncodeToString(randomBytesOfTheNewSecureToken);

        return new GeneratedSecureToken(tokenToSendInTheEmailLink, ConvertSecureTokenIntoItsHash(tokenToSendInTheEmailLink));
    }

    // Calcule l'empreinte SHA-256 d'un jeton, telle qu'elle est enregistrée en base.
    public string ConvertSecureTokenIntoItsHash(string tokenToConvertIntoItsHash)
    {
        var hashBytesOfTheSecureToken = SHA256.HashData(Encoding.UTF8.GetBytes(tokenToConvertIntoItsHash));

        return Convert.ToHexStringLower(hashBytesOfTheSecureToken);
    }
}
