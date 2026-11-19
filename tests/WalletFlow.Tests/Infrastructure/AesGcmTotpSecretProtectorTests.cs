using System.Security.Cryptography;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le chiffrement du secret de double authentification avec le vrai AES-256-GCM (SS §2 et §7), et le numéro de
// clé qui permet de changer de clé sans perdre les secrets déjà chiffrés.
public class AesGcmTotpSecretProtectorTests
{
    private const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static readonly Guid AccountId = Guid.NewGuid();

    private readonly AesGcmTotpSecretProtector protector = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateEncryptionKey());

    // Le Base64 qui suit le numéro de la clé : le nombre aléatoire, le sceau et le secret chiffré.
    private static byte[] ReadPayload(string encryptedSecret)
    {
        return Convert.FromBase64String(encryptedSecret.Substring(encryptedSecret.IndexOf('$') + 1));
    }

    [Fact]
    public void SecretComesBackIdenticalAfterEncryptionAndDecryption()
    {
        var encryptedSecret = protector.Encrypt(Secret, AccountId);

        Assert.Equal(Secret, protector.Decrypt(encryptedSecret, AccountId));
    }

    [Fact]
    public void EncryptedTextHidesTheSecretAndFitsInTheDatabaseColumn()
    {
        var encryptedSecret = protector.Encrypt(Secret, AccountId);

        Assert.DoesNotContain(Secret, encryptedSecret);
        Assert.StartsWith("k1$", encryptedSecret);
        Assert.Equal(83, encryptedSecret.Length);
    }

    // Avec un nombre aléatoire réutilisé, AES-GCM perdrait toute sa protection : chaque chiffrement doit donc en tirer un nouveau.
    [Fact]
    public void SameSecretNeverGivesTheSameEncryptedTextTwice()
    {
        var seenCiphertexts = new HashSet<string>();
        var seenNonces = new HashSet<string>();

        for (var encryptionNumber = 1; encryptionNumber <= 1000; encryptionNumber++)
        {
            var encryptedSecret = protector.Encrypt(Secret, AccountId);

            Assert.True(seenCiphertexts.Add(encryptedSecret));
            Assert.True(seenNonces.Add(Convert.ToBase64String(ReadPayload(encryptedSecret).AsSpan(0, 12))));
        }
    }

    // Quelqu'un qui écrit dans la base ne peut pas donner à sa victime un secret qu'il connaît en copiant le sien.
    [Fact]
    public void SecretCopiedOnAnotherAccountCannotBeDecrypted()
    {
        var encryptedSecret = protector.Encrypt(Secret, AccountId);

        Assert.ThrowsAny<CryptographicException>(() => protector.Decrypt(encryptedSecret, Guid.NewGuid()));
    }

    [Fact]
    public void SecretCannotBeDecryptedWithAnotherKey()
    {
        var encryptedSecret = protector.Encrypt(Secret, AccountId);
        var otherProtector = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateEncryptionKey());

        Assert.ThrowsAny<CryptographicException>(() => otherProtector.Decrypt(encryptedSecret, AccountId));
    }

    // Le moindre bit changé dans le nombre aléatoire, le sceau ou le secret chiffré est vu : le secret n'est jamais deviné.
    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(59)]
    public void SecretModifiedInTheDatabaseCannotBeDecrypted(int changedPosition)
    {
        var ciphertext = ReadPayload(protector.Encrypt(Secret, AccountId));
        ciphertext[changedPosition] = (byte)(ciphertext[changedPosition] ^ 1);

        Assert.ThrowsAny<CryptographicException>(() => protector.Decrypt("k1$" + Convert.ToBase64String(ciphertext), AccountId));
    }

    // Un texte illisible en base lève une erreur : l'API répondra 500, sans jamais prendre ce texte pour un secret.
    [Theory]
    [InlineData("")]
    [InlineData("pas-du-base64!")]
    [InlineData("AAAA")]
    public void UnreadableTextInTheDatabaseIsNeverTakenForASecret(string unreadableText)
    {
        Assert.ThrowsAny<Exception>(() => protector.Decrypt(unreadableText, AccountId));
    }

    // Sans le contrôle de la taille, une clé de 128 bits passerait : AES l'accepte, mais la SS demande 256 bits.
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void KeyOfTheWrongSizeIsRefusedAtStartup(int keyLength)
    {
        var wrongSizeKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(keyLength));

        var exception = Assert.Throws<InvalidOperationException>(() => new AesGcmTotpSecretProtector(wrongSizeKey));

        Assert.DoesNotContain(wrongSizeKey, exception.Message);
    }

    // Le message d'erreur va dans les journaux : il ne cite jamais la valeur de la clé.
    [Fact]
    public void KeyThatIsNotBase64IsRefusedAtStartupWithoutQuotingIt()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new AesGcmTotpSecretProtector("cle-secrete-mal-ecrite"));

        Assert.Contains("TwoFactorSecretEncryptionKey", exception.Message);
        Assert.DoesNotContain("cle-secrete-mal-ecrite", exception.Message);
    }

    // SS §7 : une nouvelle clé chiffre les nouveaux secrets ; l'ancienne déchiffre encore ceux d'avant, qui attendent leur
    // nouveau chiffrement.
    [Fact]
    public void NewKeyEncryptsWhileTheOldKeyStillDecrypts()
    {
        var oldKey = TwoFactorTestTools.CreateEncryptionKey();
        var oldSecret = new AesGcmTotpSecretProtector(oldKey).Encrypt(Secret, AccountId);
        var keyRing = new AesGcmTotpSecretProtector(2, TwoFactorTestTools.CreateEncryptionKey(), new Dictionary<int, string> { [1] = oldKey });

        var newSecret = keyRing.Encrypt(Secret, AccountId);

        Assert.Equal(Secret, keyRing.Decrypt(oldSecret, AccountId));
        Assert.Equal(Secret, keyRing.Decrypt(newSecret, AccountId));
        Assert.Equal("k2$", keyRing.CurrentKeyPrefix);
        Assert.StartsWith("k2$", newSecret);
        Assert.False(keyRing.UsesCurrentKey(oldSecret));
        Assert.True(keyRing.UsesCurrentKey(newSecret));
    }

    // Un secret chiffré avant les numéros de clé, sans « k1$ », se lit avec la clé 1.
    [Fact]
    public void SecretWithoutKeyNumberIsReadWithKeyOne()
    {
        var oldKey = TwoFactorTestTools.CreateEncryptionKey();
        var secretWithoutNumber = new AesGcmTotpSecretProtector(oldKey).Encrypt(Secret, AccountId).Substring("k1$".Length);
        var keyRing = new AesGcmTotpSecretProtector(2, TwoFactorTestTools.CreateEncryptionKey(), new Dictionary<int, string> { [1] = oldKey });

        Assert.Equal(Secret, keyRing.Decrypt(secretWithoutNumber, AccountId));
        Assert.False(keyRing.UsesCurrentKey(secretWithoutNumber));
    }

    // Une clé retirée de la configuration ne déchiffre plus rien, et aucun secret n'est deviné à sa place.
    [Fact]
    public void SecretOfARemovedKeyIsNeverDecrypted()
    {
        var oldSecret = protector.Encrypt(Secret, AccountId);
        var keyRing = new AesGcmTotpSecretProtector(2, TwoFactorTestTools.CreateEncryptionKey(), new Dictionary<int, string>());

        Assert.ThrowsAny<CryptographicException>(() => keyRing.Decrypt(oldSecret, AccountId));
    }

    // Changer le numéro de clé en base ne fait pas lire le secret avec une autre clé : le sceau le voit.
    [Fact]
    public void ChangedKeyNumberIsSeenByTheSeal()
    {
        var oldKey = TwoFactorTestTools.CreateEncryptionKey();
        var keyRing = new AesGcmTotpSecretProtector(2, TwoFactorTestTools.CreateEncryptionKey(), new Dictionary<int, string> { [1] = oldKey });
        var oldSecret = new AesGcmTotpSecretProtector(oldKey).Encrypt(Secret, AccountId);

        Assert.ThrowsAny<CryptographicException>(() => keyRing.Decrypt(string.Concat("k2$", oldSecret.AsSpan("k1$".Length)), AccountId));
    }

    // Un numéro de clé illisible n'est jamais deviné.
    [Theory]
    [InlineData("kx$AAAA")]
    [InlineData("k$AAAA")]
    [InlineData("$AAAA")]
    [InlineData("1$AAAA")]
    [InlineData("k-1$AAAA")]
    [InlineData("k 1$AAAA")]
    [InlineData("k99999999999$AAAA")]
    public void UnreadableKeyNumberIsNeverGuessed(string encryptedSecret)
    {
        Assert.ThrowsAny<CryptographicException>(() => protector.Decrypt(encryptedSecret, AccountId));
    }

    // Un numéro de clé qui n'est pas positif, ou donné à deux clés, empêche l'API de démarrer, sans citer aucune clé.
    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(2, -3)]
    [InlineData(2, 2)]
    public void InvalidKeyNumberIsRefusedAtStartup(int currentKeyId, int previousKeyId)
    {
        var currentKey = TwoFactorTestTools.CreateEncryptionKey();
        var previousKey = TwoFactorTestTools.CreateEncryptionKey();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AesGcmTotpSecretProtector(currentKeyId, currentKey, new Dictionary<int, string> { [previousKeyId] = previousKey }));

        Assert.DoesNotContain(currentKey, exception.Message);
        Assert.DoesNotContain(previousKey, exception.Message);
    }

    // Une ancienne clé mal écrite empêche l'API de démarrer : le message dit laquelle, sans citer sa valeur.
    [Fact]
    public void MalformedPreviousKeyIsRefusedAtStartupWithoutQuotingIt()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AesGcmTotpSecretProtector(2, TwoFactorTestTools.CreateEncryptionKey(), new Dictionary<int, string> { [1] = "ancienne-cle-mal-ecrite" }));

        Assert.Contains("TwoFactorSecretPreviousKeys:1", exception.Message);
        Assert.DoesNotContain("ancienne-cle-mal-ecrite", exception.Message);
    }
}
