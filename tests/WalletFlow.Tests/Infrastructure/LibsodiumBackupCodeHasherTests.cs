using System.Text;
using Sodium;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le vrai hachage des codes de secours avec libsodium (SS §2).
public class LibsodiumBackupCodeHasherTests
{
    private const string BackupCode = "ABCDE12345";

    private readonly LibsodiumBackupCodeHasher hasher = RealArgonHashers.CreateBackupCodeHasher();

    [Fact]
    public async Task SecureHashSaysHowItWasComputedAndFitsInTheDatabaseColumn()
    {
        var hash = await hasher.HashAsync(BackupCode, Guid.NewGuid());

        Assert.StartsWith(LibsodiumBackupCodeHasher.HashPrefix, hash);
        Assert.DoesNotContain(BackupCode, hash);

        // La colonne empreinte_code_secours accepte 255 caractères (MPD).
        Assert.InRange(hash.Length, 1, 255);
    }

    // C'est ce qui permet de ne faire qu'un seul calcul par code tapé.
    [Fact]
    public async Task SameCodeOfTheSameAccountAlwaysGivesTheSameSecureHash()
    {
        var accountId = Guid.NewGuid();

        var firstHash = await hasher.HashAsync(BackupCode, accountId);
        var secondHash = await hasher.HashAsync(BackupCode, accountId);

        Assert.Equal(firstHash, secondHash);
    }

    // Le sel est propre à chaque compte : le même code n'a pas la même empreinte sur deux comptes.
    [Fact]
    public async Task SameCodeOfTwoAccountsGivesTwoDifferentSecureHashes()
    {
        var firstAccountHash = await hasher.HashAsync(BackupCode, Guid.NewGuid());
        var secondAccountHash = await hasher.HashAsync(BackupCode, Guid.NewGuid());

        Assert.NotEqual(firstAccountHash, secondAccountHash);
    }

    [Fact]
    public async Task TwoDifferentCodesOfTheSameAccountGiveTwoDifferentSecureHashes()
    {
        var accountId = Guid.NewGuid();

        var firstCodeHash = await hasher.HashAsync(BackupCode, accountId);
        var secondCodeHash = await hasher.HashAsync("ABCDE12346", accountId);

        Assert.NotEqual(firstCodeHash, secondCodeHash);
    }

    // Fige l'algorithme et ses réglages : Argon2id, et non Argon2i que Sodium.Core prend par défaut pour ce calcul,
    // avec 32 Mio de mémoire et 4 passes, comme les mots de passe.
    [Fact]
    public async Task SecureHashIsComputedWithArgon2idAndTheSettingsOfThePasswords()
    {
        var accountId = Guid.NewGuid();

        var hash = await hasher.HashAsync(BackupCode, accountId);

        var expectedBytes = PasswordHash.ArgonHashBinary(
            Encoding.UTF8.GetBytes(BackupCode),
            accountId.ToByteArray(),
            PasswordHash.StrengthArgon.Interactive,
            32,
            PasswordHash.ArgonAlgorithm.Argon_2ID13);
        Assert.Equal(LibsodiumBackupCodeHasher.HashPrefix + Convert.ToBase64String(expectedBytes), hash);
    }
}
