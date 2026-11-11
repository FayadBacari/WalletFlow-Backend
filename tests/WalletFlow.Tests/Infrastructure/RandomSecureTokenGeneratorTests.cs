using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la création des jetons des liens envoyés par e-mail (SS §2).
public class RandomSecureTokenGeneratorTests
{
    private readonly RandomSecureTokenGenerator generator = new RandomSecureTokenGenerator();

    [Fact]
    public void BackupCodeHasTheExpectedFormatAndNeverComesBackTwice()
    {
        var backupCodes = new HashSet<string>();

        for (var codeNumber = 1; codeNumber <= 1000; codeNumber++)
        {
            var backupCode = generator.GenerateBackupCode();

            Assert.True(TwoFactorAuthenticationRules.HasBackupCodeFormat(backupCode));
            Assert.True(backupCodes.Add(backupCode));
        }
    }

    // Sur 10 000 caractères, chacun des 32 caractères permis doit sortir : un tirage réduit à quelques-uns donnerait des codes faciles à deviner.
    [Fact]
    public void EveryAllowedCharacterComesOutOfTheBackupCodes()
    {
        var seenCharacters = new HashSet<char>();

        for (var codeNumber = 1; codeNumber <= 1000; codeNumber++)
        {
            seenCharacters.UnionWith(generator.GenerateBackupCode());
        }

        Assert.Equal(32, seenCharacters.Count);
    }

    [Fact]
    public void TokenContainsAtLeast128RandomBitsAndCanBePlacedInLink()
    {
        var generatedToken = generator.GenerateToken();

        var randomBytes = Base64Url.DecodeFromChars(generatedToken.Token);
        Assert.True(randomBytes.Length * 8 >= 128);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), generatedToken.Token);
    }

    [Fact]
    public void StoredHashIsTheSha256OfTheTokenAndFitsInTheDatabaseColumn()
    {
        var generatedToken = generator.GenerateToken();

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(generatedToken.Token));
        Assert.Equal(Convert.ToHexStringLower(hashBytes), generatedToken.Hash);

        // La colonne empreinte_jeton contient exactement 64 caractères (MPD).
        Assert.Equal(64, generatedToken.Hash.Length);
    }

    // Le jeton reçu dans le lien doit redonner l'empreinte enregistrée : sinon, aucun lien ne marcherait.
    [Fact]
    public void TokenReceivedInTheLinkGivesBackTheStoredHash()
    {
        var generatedToken = generator.GenerateToken();

        var hash = generator.HashToken(generatedToken.Token);

        Assert.Equal(generatedToken.Hash, hash);
    }

    [Fact]
    public void TwoGeneratedTokensAreAlwaysDifferentFromEachOther()
    {
        var firstToken = generator.GenerateToken();
        var secondToken = generator.GenerateToken();

        Assert.NotEqual(firstToken.Token, secondToken.Token);
    }
}
