using System.Security.Cryptography;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le chiffrement du secret de double authentification avec le vrai AES-256-GCM (SS §2 et §7).
public class AesGcmTotpSecretProtectorTests
{
    private const string SecretOfThePhoneEncryptedInTheseTests = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static readonly Guid IdentifierOfTheAccountInTheseTests = Guid.NewGuid();

    private readonly AesGcmTotpSecretProtector protectorBeingTestedWithARealKey = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateNewEncryptionKeyForOneTest());

    [Fact]
    public void SecretComesBackIdenticalAfterEncryptionAndDecryption()
    {
        var secretOfThePhoneEncryptedForTheDatabase = protectorBeingTestedWithARealKey.EncryptSecretOfThisUserAccount(SecretOfThePhoneEncryptedInTheseTests, IdentifierOfTheAccountInTheseTests);

        Assert.Equal(SecretOfThePhoneEncryptedInTheseTests, protectorBeingTestedWithARealKey.DecryptSecretOfThisUserAccount(secretOfThePhoneEncryptedForTheDatabase, IdentifierOfTheAccountInTheseTests));
    }

    [Fact]
    public void EncryptedTextHidesTheSecretAndFitsInTheDatabaseColumn()
    {
        var secretOfThePhoneEncryptedForTheDatabase = protectorBeingTestedWithARealKey.EncryptSecretOfThisUserAccount(SecretOfThePhoneEncryptedInTheseTests, IdentifierOfTheAccountInTheseTests);

        Assert.DoesNotContain(SecretOfThePhoneEncryptedInTheseTests, secretOfThePhoneEncryptedForTheDatabase);
        Assert.Equal(80, secretOfThePhoneEncryptedForTheDatabase.Length);
    }

    // Avec un nombre aléatoire réutilisé, AES-GCM perdrait toute sa protection : chaque chiffrement doit donc en tirer un nouveau.
    [Fact]
    public void SameSecretNeverGivesTheSameEncryptedTextTwice()
    {
        var encryptedTextsAlreadySeenInThisTest = new HashSet<string>();
        var randomNoncesAlreadySeenInThisTest = new HashSet<string>();

        for (var numberOfTheEncryptionBeingDone = 1; numberOfTheEncryptionBeingDone <= 1000; numberOfTheEncryptionBeingDone++)
        {
            var secretOfThePhoneEncryptedForTheDatabase = protectorBeingTestedWithARealKey.EncryptSecretOfThisUserAccount(SecretOfThePhoneEncryptedInTheseTests, IdentifierOfTheAccountInTheseTests);

            Assert.True(encryptedTextsAlreadySeenInThisTest.Add(secretOfThePhoneEncryptedForTheDatabase));
            Assert.True(randomNoncesAlreadySeenInThisTest.Add(Convert.ToBase64String(Convert.FromBase64String(secretOfThePhoneEncryptedForTheDatabase).AsSpan(0, 12))));
        }
    }

    // Quelqu'un qui écrit dans la base ne peut pas donner à sa victime un secret qu'il connaît en copiant le sien.
    [Fact]
    public void SecretCopiedOnAnotherAccountCannotBeDecrypted()
    {
        var secretOfThePhoneEncryptedForTheDatabase = protectorBeingTestedWithARealKey.EncryptSecretOfThisUserAccount(SecretOfThePhoneEncryptedInTheseTests, IdentifierOfTheAccountInTheseTests);

        Assert.ThrowsAny<CryptographicException>(() => protectorBeingTestedWithARealKey.DecryptSecretOfThisUserAccount(secretOfThePhoneEncryptedForTheDatabase, Guid.NewGuid()));
    }

    [Fact]
    public void SecretCannotBeDecryptedWithAnotherKey()
    {
        var secretOfThePhoneEncryptedForTheDatabase = protectorBeingTestedWithARealKey.EncryptSecretOfThisUserAccount(SecretOfThePhoneEncryptedInTheseTests, IdentifierOfTheAccountInTheseTests);
        var protectorOfTheSecretsWithAnotherKey = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateNewEncryptionKeyForOneTest());

        Assert.ThrowsAny<CryptographicException>(() => protectorOfTheSecretsWithAnotherKey.DecryptSecretOfThisUserAccount(secretOfThePhoneEncryptedForTheDatabase, IdentifierOfTheAccountInTheseTests));
    }

    // Le moindre bit changé dans le nombre aléatoire, le sceau ou le secret chiffré est vu : le secret n'est jamais deviné.
    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(59)]
    public void SecretModifiedInTheDatabaseCannotBeDecrypted(int positionOfTheByteChangedInThisTest)
    {
        var bytesOfTheSecretOnceItIsEncrypted = Convert.FromBase64String(protectorBeingTestedWithARealKey.EncryptSecretOfThisUserAccount(SecretOfThePhoneEncryptedInTheseTests, IdentifierOfTheAccountInTheseTests));
        bytesOfTheSecretOnceItIsEncrypted[positionOfTheByteChangedInThisTest] = (byte)(bytesOfTheSecretOnceItIsEncrypted[positionOfTheByteChangedInThisTest] ^ 1);

        Assert.ThrowsAny<CryptographicException>(() => protectorBeingTestedWithARealKey.DecryptSecretOfThisUserAccount(Convert.ToBase64String(bytesOfTheSecretOnceItIsEncrypted), IdentifierOfTheAccountInTheseTests));
    }

    // Un texte illisible en base lève une erreur : l'API répondra 500, sans jamais prendre ce texte pour un secret.
    [Theory]
    [InlineData("")]
    [InlineData("pas-du-base64!")]
    [InlineData("AAAA")]
    public void UnreadableTextInTheDatabaseIsNeverTakenForASecret(string unreadableTextFromTheDatabaseInThisTest)
    {
        Assert.ThrowsAny<Exception>(() => protectorBeingTestedWithARealKey.DecryptSecretOfThisUserAccount(unreadableTextFromTheDatabaseInThisTest, IdentifierOfTheAccountInTheseTests));
    }

    // Sans le contrôle de la taille, une clé de 128 bits passerait : AES l'accepte, mais la SS demande 256 bits.
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void KeyOfTheWrongSizeIsRefusedAtStartup(int numberOfBytesInTheKeyOfThisTest)
    {
        var encryptionKeyOfTheWrongSizeInThisTest = Convert.ToBase64String(RandomNumberGenerator.GetBytes(numberOfBytesInTheKeyOfThisTest));

        var errorRaisedByTheProtectorOfTheSecrets = Assert.Throws<InvalidOperationException>(() => new AesGcmTotpSecretProtector(encryptionKeyOfTheWrongSizeInThisTest));

        Assert.DoesNotContain(encryptionKeyOfTheWrongSizeInThisTest, errorRaisedByTheProtectorOfTheSecrets.Message);
    }

    // Le message d'erreur va dans les journaux : il ne cite jamais la valeur de la clé.
    [Fact]
    public void KeyThatIsNotBase64IsRefusedAtStartupWithoutQuotingIt()
    {
        var errorRaisedByTheProtectorOfTheSecrets = Assert.Throws<InvalidOperationException>(() => new AesGcmTotpSecretProtector("cle-secrete-mal-ecrite"));

        Assert.Contains("TwoFactorSecretEncryptionKey", errorRaisedByTheProtectorOfTheSecrets.Message);
        Assert.DoesNotContain("cle-secrete-mal-ecrite", errorRaisedByTheProtectorOfTheSecrets.Message);
    }
}
