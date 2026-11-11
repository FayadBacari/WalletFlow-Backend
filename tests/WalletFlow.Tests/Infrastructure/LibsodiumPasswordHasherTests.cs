using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le vrai hachage des mots de passe avec libsodium (SS §2).
public class LibsodiumPasswordHasherTests
{
    private const string Password = "Mot-De-Passe-Valide-1";

    private readonly LibsodiumPasswordHasher hasher = RealArgonHashers.CreatePasswordHasher();

    [Fact]
    public async Task SecureHashUsesArgon2idAndFitsInTheDatabaseColumn()
    {
        var hash = await hasher.HashAsync(Password);

        Assert.StartsWith("$argon2id$", hash);
        Assert.DoesNotContain(Password, hash);

        // La colonne mot_de_passe_hash accepte 255 caractères (MPD).
        Assert.InRange(hash.Length, 1, 255);
    }

    // Fige les réglages : une mise à jour de la bibliothèque ne doit pas les affaiblir sans qu'on le voie.
    [Fact]
    public async Task SecureHashUsesThirtyTwoMebibytesOfMemoryAndFourPasses()
    {
        var hash = await hasher.HashAsync(Password);

        Assert.Contains("$m=32768,t=4,p=1$", hash);
    }

    [Fact]
    public async Task RightPasswordMatchesItsSecureHashAndWrongPasswordDoesNot()
    {
        var hash = await hasher.HashAsync(Password);

        Assert.True(await hasher.VerifyAsync(Password, hash));
        Assert.False(await hasher.VerifyAsync("Autre-Mot-De-Passe-2", hash));
    }

    [Fact]
    public async Task SamePasswordHashedTwiceGivesTwoDifferentSecureHashes()
    {
        var firstHash = await hasher.HashAsync(Password);
        var secondHash = await hasher.HashAsync(Password);

        Assert.NotEqual(firstHash, secondHash);
    }
}
