using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le vrai hachage des mots de passe avec libsodium (SS §2).
public class LibsodiumPasswordHasherTests
{
    private const string PasswordHashedByTheHasherInTheseTests = "Mot-De-Passe-Valide-1";

    private readonly LibsodiumPasswordHasher hasherBeingTestedWithTheRealLibsodium = new LibsodiumPasswordHasher();

    [Fact]
    public void SecureHashUsesArgon2idAndFitsInTheDatabaseColumn()
    {
        var secureHashOfThePasswordBeingTested = hasherBeingTestedWithTheRealLibsodium.ConvertPasswordIntoItsSecureHash(PasswordHashedByTheHasherInTheseTests);

        Assert.StartsWith("$argon2id$", secureHashOfThePasswordBeingTested);
        Assert.DoesNotContain(PasswordHashedByTheHasherInTheseTests, secureHashOfThePasswordBeingTested);

        // La colonne mot_de_passe_hash accepte 255 caractères (MPD).
        Assert.InRange(secureHashOfThePasswordBeingTested.Length, 1, 255);
    }

    // Fige les réglages : une mise à jour de la bibliothèque ne doit pas les affaiblir sans qu'on le voie.
    [Fact]
    public void SecureHashUsesThirtyTwoMebibytesOfMemoryAndFourPasses()
    {
        var secureHashOfThePasswordBeingTested = hasherBeingTestedWithTheRealLibsodium.ConvertPasswordIntoItsSecureHash(PasswordHashedByTheHasherInTheseTests);

        Assert.Contains("$m=32768,t=4,p=1$", secureHashOfThePasswordBeingTested);
    }

    [Fact]
    public void RightPasswordMatchesItsSecureHashAndWrongPasswordDoesNot()
    {
        var secureHashOfThePasswordBeingTested = hasherBeingTestedWithTheRealLibsodium.ConvertPasswordIntoItsSecureHash(PasswordHashedByTheHasherInTheseTests);

        Assert.True(hasherBeingTestedWithTheRealLibsodium.CheckThatPasswordMatchesItsSecureHash(PasswordHashedByTheHasherInTheseTests, secureHashOfThePasswordBeingTested));
        Assert.False(hasherBeingTestedWithTheRealLibsodium.CheckThatPasswordMatchesItsSecureHash("Autre-Mot-De-Passe-2", secureHashOfThePasswordBeingTested));
    }

    [Fact]
    public void SamePasswordHashedTwiceGivesTwoDifferentSecureHashes()
    {
        var firstSecureHashOfTheSamePassword = hasherBeingTestedWithTheRealLibsodium.ConvertPasswordIntoItsSecureHash(PasswordHashedByTheHasherInTheseTests);
        var secondSecureHashOfTheSamePassword = hasherBeingTestedWithTheRealLibsodium.ConvertPasswordIntoItsSecureHash(PasswordHashedByTheHasherInTheseTests);

        Assert.NotEqual(firstSecureHashOfTheSamePassword, secondSecureHashOfTheSamePassword);
    }
}
