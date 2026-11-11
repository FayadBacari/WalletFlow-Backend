using System.Security.Cryptography;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le chiffrement du secret de double authentification avec le vrai AES-256-GCM (SS §2 et §7).
public class AesGcmTotpSecretProtectorTests
{
    private const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static readonly Guid AccountId = Guid.NewGuid();

    private readonly AesGcmTotpSecretProtector protector = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateEncryptionKey());

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
        Assert.Equal(80, encryptedSecret.Length);
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
            Assert.True(seenNonces.Add(Convert.ToBase64String(Convert.FromBase64String(encryptedSecret).AsSpan(0, 12))));
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
        var ciphertext = Convert.FromBase64String(protector.Encrypt(Secret, AccountId));
        ciphertext[changedPosition] = (byte)(ciphertext[changedPosition] ^ 1);

        Assert.ThrowsAny<CryptographicException>(() => protector.Decrypt(Convert.ToBase64String(ciphertext), AccountId));
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
}
