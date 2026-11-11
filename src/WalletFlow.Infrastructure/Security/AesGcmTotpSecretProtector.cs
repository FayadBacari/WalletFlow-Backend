using System.Security.Cryptography;
using System.Text;
using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Infrastructure.Security;

// Chiffre le secret de double authentification avec AES-256-GCM, fourni par .NET (SS §2, §7 et §13).
// L'identifiant du compte entre dans le calcul : un secret copié sur un autre compte en base ne se déchiffre pas.
public class AesGcmTotpSecretProtector : ITotpSecretProtector
{
    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly byte[] encryptionKey;

    // Reçoit la clé écrite en Base64. Une clé mal formée empêche l'API de démarrer : le message ne cite jamais sa valeur.
    public AesGcmTotpSecretProtector(string base64Key)
    {
        encryptionKey = DecodeKey(base64Key);
    }

    // Transforme la clé en octets, et vérifie qu'elle fait bien 256 bits.
    private static byte[] DecodeKey(string base64Key)
    {
        try
        {
            var keyBytes = Convert.FromBase64String(base64Key);

            if (keyBytes.Length == KeyLength)
            {
                return keyBytes;
            }
        }
        catch (FormatException)
        {
            // La clé n'est pas du Base64 : même refus que pour une clé de la mauvaise taille, juste en dessous.
        }

        throw new InvalidOperationException("Le réglage « TwoFactorSecretEncryptionKey » doit contenir 32 octets écrits en Base64.");
    }

    // Chiffre le secret avec un nombre aléatoire tiré pour l'occasion. Le texte obtenu contient, dans l'ordre : ce nombre, le sceau et le secret chiffré.
    public string Encrypt(string secret, Guid accountId)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var tag = new byte[TagLength];
        var ciphertext = new byte[plaintext.Length];

        using var aesGcm = new AesGcm(encryptionKey, TagLength);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, accountId.ToByteArray());

        var storedBytes = new byte[NonceLength + TagLength + ciphertext.Length];
        nonce.CopyTo(storedBytes, 0);
        tag.CopyTo(storedBytes, NonceLength);
        ciphertext.CopyTo(storedBytes, NonceLength + TagLength);

        return Convert.ToBase64String(storedBytes);
    }

    // Déchiffre le secret. Le sceau échoue si la clé a changé, si le texte a été modifié, ou s'il appartient à un autre compte.
    public string Decrypt(string encryptedSecret, Guid accountId)
    {
        var storedBytes = Convert.FromBase64String(encryptedSecret);
        var nonce = storedBytes.AsSpan(0, NonceLength);
        var tag = storedBytes.AsSpan(NonceLength, TagLength);
        var ciphertext = storedBytes.AsSpan(NonceLength + TagLength);
        var plaintext = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(encryptionKey, TagLength);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, accountId.ToByteArray());

        return Encoding.UTF8.GetString(plaintext);
    }
}
