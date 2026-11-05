using System.Security.Cryptography;
using System.Text;
using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Infrastructure.Security;

// Chiffre le secret de double authentification avec AES-256-GCM, fourni par .NET (SS §2, §7 et §13).
// L'identifiant du compte entre dans le calcul : un secret copié sur un autre compte en base ne se déchiffre pas.
public class AesGcmTotpSecretProtector : ITotpSecretProtector
{
    private const int NumberOfBytesInTheEncryptionKey = 32;
    private const int NumberOfBytesInTheRandomNonce = 12;
    private const int NumberOfBytesInTheAuthenticationTag = 16;

    private readonly byte[] keyThatEncryptsTheTotpSecrets;

    // Reçoit la clé écrite en Base64. Une clé mal formée empêche l'API de démarrer : le message ne cite jamais sa valeur.
    public AesGcmTotpSecretProtector(string encryptionKeyWrittenInBase64)
    {
        keyThatEncryptsTheTotpSecrets = ConvertEncryptionKeyIntoItsBytes(encryptionKeyWrittenInBase64);
    }

    // Transforme la clé en octets, et vérifie qu'elle fait bien 256 bits.
    private static byte[] ConvertEncryptionKeyIntoItsBytes(string encryptionKeyWrittenInBase64)
    {
        try
        {
            var bytesOfTheKeyThatEncryptsTheSecrets = Convert.FromBase64String(encryptionKeyWrittenInBase64);

            if (bytesOfTheKeyThatEncryptsTheSecrets.Length == NumberOfBytesInTheEncryptionKey)
            {
                return bytesOfTheKeyThatEncryptsTheSecrets;
            }
        }
        catch (FormatException)
        {
            // La clé n'est pas du Base64 : même refus que pour une clé de la mauvaise taille, juste en dessous.
        }

        throw new InvalidOperationException("Le réglage « TwoFactorSecretEncryptionKey » doit contenir 32 octets écrits en Base64.");
    }

    // Chiffre le secret avec un nombre aléatoire tiré pour l'occasion. Le texte obtenu contient, dans l'ordre : ce nombre, le sceau et le secret chiffré.
    public string EncryptSecretOfThisUserAccount(string secretSharedWithTheAuthenticatorApplication, Guid identifierOfTheAccountOwningTheSecret)
    {
        var bytesOfTheSecretInClear = Encoding.UTF8.GetBytes(secretSharedWithTheAuthenticatorApplication);
        var bytesOfTheRandomNonceOfTheEncryption = RandomNumberGenerator.GetBytes(NumberOfBytesInTheRandomNonce);
        var bytesOfTheAuthenticationTagOfTheSecret = new byte[NumberOfBytesInTheAuthenticationTag];
        var bytesOfTheSecretOnceItIsEncrypted = new byte[bytesOfTheSecretInClear.Length];

        using var encryptionToolThatHoldsTheSecretsKey = new AesGcm(keyThatEncryptsTheTotpSecrets, NumberOfBytesInTheAuthenticationTag);
        encryptionToolThatHoldsTheSecretsKey.Encrypt(bytesOfTheRandomNonceOfTheEncryption, bytesOfTheSecretInClear, bytesOfTheSecretOnceItIsEncrypted, bytesOfTheAuthenticationTagOfTheSecret, identifierOfTheAccountOwningTheSecret.ToByteArray());

        var bytesOfTheSecretWrittenInTheDatabase = new byte[NumberOfBytesInTheRandomNonce + NumberOfBytesInTheAuthenticationTag + bytesOfTheSecretOnceItIsEncrypted.Length];
        bytesOfTheRandomNonceOfTheEncryption.CopyTo(bytesOfTheSecretWrittenInTheDatabase, 0);
        bytesOfTheAuthenticationTagOfTheSecret.CopyTo(bytesOfTheSecretWrittenInTheDatabase, NumberOfBytesInTheRandomNonce);
        bytesOfTheSecretOnceItIsEncrypted.CopyTo(bytesOfTheSecretWrittenInTheDatabase, NumberOfBytesInTheRandomNonce + NumberOfBytesInTheAuthenticationTag);

        return Convert.ToBase64String(bytesOfTheSecretWrittenInTheDatabase);
    }

    // Déchiffre le secret. Le sceau échoue si la clé a changé, si le texte a été modifié, ou s'il appartient à un autre compte.
    public string DecryptSecretOfThisUserAccount(string secretOfThePhoneEncryptedInTheDatabase, Guid identifierOfTheAccountOwningTheSecret)
    {
        var bytesOfTheSecretReadInTheDatabase = Convert.FromBase64String(secretOfThePhoneEncryptedInTheDatabase);
        var bytesOfTheRandomNonceOfTheEncryption = bytesOfTheSecretReadInTheDatabase.AsSpan(0, NumberOfBytesInTheRandomNonce);
        var bytesOfTheAuthenticationTagOfTheSecret = bytesOfTheSecretReadInTheDatabase.AsSpan(NumberOfBytesInTheRandomNonce, NumberOfBytesInTheAuthenticationTag);
        var bytesOfTheSecretOnceItIsEncrypted = bytesOfTheSecretReadInTheDatabase.AsSpan(NumberOfBytesInTheRandomNonce + NumberOfBytesInTheAuthenticationTag);
        var bytesOfTheSecretInClear = new byte[bytesOfTheSecretOnceItIsEncrypted.Length];

        using var encryptionToolThatHoldsTheSecretsKey = new AesGcm(keyThatEncryptsTheTotpSecrets, NumberOfBytesInTheAuthenticationTag);
        encryptionToolThatHoldsTheSecretsKey.Decrypt(bytesOfTheRandomNonceOfTheEncryption, bytesOfTheSecretOnceItIsEncrypted, bytesOfTheAuthenticationTagOfTheSecret, bytesOfTheSecretInClear, identifierOfTheAccountOwningTheSecret.ToByteArray());

        return Encoding.UTF8.GetString(bytesOfTheSecretInClear);
    }
}
