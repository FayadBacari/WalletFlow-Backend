using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la création des jetons des liens envoyés par e-mail (SS §2).
public class RandomSecureTokenGeneratorTests
{
    private readonly RandomSecureTokenGenerator generatorBeingTestedWithRealRandomness = new RandomSecureTokenGenerator();

    [Fact]
    public void TokenContainsAtLeast128RandomBitsAndCanBePlacedInLink()
    {
        var generatedSecureTokenBeingTestedHere = generatorBeingTestedWithRealRandomness.GenerateNewRandomSecureTokenWithItsHash();

        var randomBytesInsideTheGeneratedToken = Base64Url.DecodeFromChars(generatedSecureTokenBeingTestedHere.TokenGivenOnlyToTheVisitor);
        Assert.True(randomBytesInsideTheGeneratedToken.Length * 8 >= 128);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), generatedSecureTokenBeingTestedHere.TokenGivenOnlyToTheVisitor);
    }

    [Fact]
    public void StoredHashIsTheSha256OfTheTokenAndFitsInTheDatabaseColumn()
    {
        var generatedSecureTokenBeingTestedHere = generatorBeingTestedWithRealRandomness.GenerateNewRandomSecureTokenWithItsHash();

        var hashBytesComputedByThisTest = SHA256.HashData(Encoding.UTF8.GetBytes(generatedSecureTokenBeingTestedHere.TokenGivenOnlyToTheVisitor));
        Assert.Equal(Convert.ToHexStringLower(hashBytesComputedByThisTest), generatedSecureTokenBeingTestedHere.HashToStoreInTheDatabase);

        // La colonne empreinte_jeton contient exactement 64 caractères (MPD).
        Assert.Equal(64, generatedSecureTokenBeingTestedHere.HashToStoreInTheDatabase.Length);
    }

    // Le jeton reçu dans le lien doit redonner l'empreinte enregistrée : sinon, aucun lien ne marcherait.
    [Fact]
    public void TokenReceivedInTheLinkGivesBackTheStoredHash()
    {
        var generatedSecureTokenBeingTestedHere = generatorBeingTestedWithRealRandomness.GenerateNewRandomSecureTokenWithItsHash();

        var hashComputedFromTheTokenOfTheLink = generatorBeingTestedWithRealRandomness.ConvertSecureTokenIntoItsHash(generatedSecureTokenBeingTestedHere.TokenGivenOnlyToTheVisitor);

        Assert.Equal(generatedSecureTokenBeingTestedHere.HashToStoreInTheDatabase, hashComputedFromTheTokenOfTheLink);
    }

    [Fact]
    public void TwoGeneratedTokensAreAlwaysDifferentFromEachOther()
    {
        var firstGeneratedTokenBeingComparedHere = generatorBeingTestedWithRealRandomness.GenerateNewRandomSecureTokenWithItsHash();
        var secondGeneratedTokenBeingComparedHere = generatorBeingTestedWithRealRandomness.GenerateNewRandomSecureTokenWithItsHash();

        Assert.NotEqual(firstGeneratedTokenBeingComparedHere.TokenGivenOnlyToTheVisitor, secondGeneratedTokenBeingComparedHere.TokenGivenOnlyToTheVisitor);
    }
}
