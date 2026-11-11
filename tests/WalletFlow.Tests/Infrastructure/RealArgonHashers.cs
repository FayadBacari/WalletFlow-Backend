using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Infrastructure;

// Donne aux tests les vrais hachages Argon2id, avec un seul limiteur pour tous, comme dans l'API.
public static class RealArgonHashers
{
    private static readonly ArgonComputationsLimiter ArgonLimiter = new ArgonComputationsLimiter(Environment.ProcessorCount);

    // Crée le vrai hachage des mots de passe.
    public static LibsodiumPasswordHasher CreatePasswordHasher()
    {
        return new LibsodiumPasswordHasher(ArgonLimiter);
    }

    // Crée le vrai hachage des codes de secours.
    public static LibsodiumBackupCodeHasher CreateBackupCodeHasher()
    {
        return new LibsodiumBackupCodeHasher(ArgonLimiter);
    }
}
