using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Infrastructure.Security;

// Chiffre le secret de double authentification avec AES-256-GCM, fourni par .NET (SS §2, §7 et §13).
// L'identifiant du compte entre dans le calcul : un secret copié sur un autre compte en base ne se déchiffre pas.
// Chaque texte chiffré commence par le numéro de sa clé, par exemple « k2$ ». La clé actuelle chiffre ; les anciennes ne
// servent plus qu'à déchiffrer, le temps que la tâche quotidienne rechiffre chaque secret avec la clé actuelle (SS §7).
// Un texte sans numéro date d'avant les numéros : il est chiffré avec la clé 1.
public class AesGcmTotpSecretProtector : ITotpSecretProtector
{
    public const string CurrentKeySetting = "TwoFactorSecretEncryptionKey";
    public const string CurrentKeyIdSetting = "TwoFactorSecretEncryptionKeyId";
    public const string PreviousKeysSetting = "TwoFactorSecretPreviousKeys";
    public const int FirstKeyId = 1;

    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const char KeyIdMark = 'k';
    private const char KeyIdEnd = '$';

    private readonly int currentKeyId;
    private readonly Dictionary<int, byte[]> keysById = new Dictionary<int, byte[]>();

    // Reçoit une seule clé, écrite en Base64 : c'est la clé 1.
    public AesGcmTotpSecretProtector(string base64Key)
        : this(FirstKeyId, base64Key, new Dictionary<int, string>())
    {
    }

    // Reçoit la clé actuelle et son numéro, puis les anciennes clés par numéro. Une clé mal formée, un numéro qui n'est pas
    // positif ou un numéro donné deux fois empêchent l'API de démarrer : le message ne cite jamais la valeur d'une clé.
    public AesGcmTotpSecretProtector(int currentKeyId, string currentBase64Key, IReadOnlyDictionary<int, string> previousBase64Keys)
    {
        ThrowIfInvalidKeyId(currentKeyId, CurrentKeyIdSetting);
        keysById[currentKeyId] = DecodeKey(currentBase64Key, CurrentKeySetting);

        foreach (var previousKey in previousBase64Keys)
        {
            var settingName = PreviousKeysSetting + ":" + previousKey.Key;
            ThrowIfInvalidKeyId(previousKey.Key, settingName);

            if (!keysById.TryAdd(previousKey.Key, DecodeKey(previousKey.Value, settingName)))
            {
                throw new InvalidOperationException("Le numéro de clé " + previousKey.Key + " est déjà celui de la clé actuelle.");
            }
        }

        this.currentKeyId = currentKeyId;
        CurrentKeyPrefix = KeyIdMark + currentKeyId.ToString(CultureInfo.InvariantCulture) + KeyIdEnd;
    }

    // Le début des textes chiffrés avec la clé actuelle.
    public string CurrentKeyPrefix { get; }

    // Renvoie vrai si ce texte est chiffré avec la clé actuelle.
    public bool UsesCurrentKey(string encryptedSecret)
    {
        return encryptedSecret.StartsWith(CurrentKeyPrefix, StringComparison.Ordinal);
    }

    // Refuse un numéro de clé qui n'est pas un entier positif.
    private static void ThrowIfInvalidKeyId(int keyId, string settingName)
    {
        if (keyId < FirstKeyId)
        {
            throw new InvalidOperationException("Le numéro de clé de « " + settingName + " » doit être un entier positif.");
        }
    }

    // Transforme la clé en octets, et vérifie qu'elle fait bien 256 bits.
    private static byte[] DecodeKey(string base64Key, string settingName)
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

        throw new InvalidOperationException("Le réglage « " + settingName + " » doit contenir 32 octets écrits en Base64.");
    }

    // Chiffre le secret avec la clé actuelle et un nombre aléatoire tiré pour l'occasion. Le texte obtenu contient, dans
    // l'ordre : le numéro de la clé, puis en Base64 ce nombre, le sceau et le secret chiffré.
    public string Encrypt(string secret, Guid accountId)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var tag = new byte[TagLength];
        var ciphertext = new byte[plaintext.Length];

        using var aesGcm = new AesGcm(keysById[currentKeyId], TagLength);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, accountId.ToByteArray());

        var storedBytes = new byte[NonceLength + TagLength + ciphertext.Length];
        nonce.CopyTo(storedBytes, 0);
        tag.CopyTo(storedBytes, NonceLength);
        ciphertext.CopyTo(storedBytes, NonceLength + TagLength);

        return CurrentKeyPrefix + Convert.ToBase64String(storedBytes);
    }

    // Déchiffre le secret avec la clé dont il porte le numéro. Le sceau échoue si la clé a changé, si le texte a été modifié,
    // ou s'il appartient à un autre compte. Une clé retirée de la configuration lève aussi une erreur : le secret n'est jamais
    // deviné.
    public string Decrypt(string encryptedSecret, Guid accountId)
    {
        var (keyId, base64Payload) = SplitKeyId(encryptedSecret);

        if (!keysById.TryGetValue(keyId, out var key))
        {
            throw new CryptographicException("La clé " + keyId + " des secrets de double authentification n'est pas configurée.");
        }

        var storedBytes = Convert.FromBase64String(base64Payload);
        var nonce = storedBytes.AsSpan(0, NonceLength);
        var tag = storedBytes.AsSpan(NonceLength, TagLength);
        var ciphertext = storedBytes.AsSpan(NonceLength + TagLength);
        var plaintext = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(key, TagLength);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, accountId.ToByteArray());

        return Encoding.UTF8.GetString(plaintext);
    }

    // Sépare le numéro de la clé du reste. Le Base64 n'a jamais de « $ » : un texte sans « $ » date d'avant les numéros.
    private static (int KeyId, string Base64Payload) SplitKeyId(string encryptedSecret)
    {
        var end = encryptedSecret.IndexOf(KeyIdEnd);
        if (end < 0)
        {
            return (FirstKeyId, encryptedSecret);
        }

        if (end < 2
            || encryptedSecret[0] != KeyIdMark
            || !int.TryParse(encryptedSecret.AsSpan(1, end - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var keyId))
        {
            throw new CryptographicException("Le numéro de clé du secret chiffré est illisible.");
        }

        return (keyId, encryptedSecret.Substring(end + 1));
    }
}
