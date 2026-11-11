using Sodium;
using WalletFlow.Application.Security;

namespace WalletFlow.Infrastructure.Security;

// Hache les mots de passe en Argon2id avec libsodium, une bibliothèque reconnue (SS §2 et §13).
// L'empreinte contient son sel et ses réglages : deux utilisateurs avec le même mot de passe
// ont donc deux empreintes différentes.
public class LibsodiumPasswordHasher : IPasswordHasher
{
    private readonly ArgonComputationsLimiter argonLimiter;

    // Reçoit le limiteur partagé par tous les calculs Argon2id du serveur.
    public LibsodiumPasswordHasher(ArgonComputationsLimiter argonLimiter)
    {
        this.argonLimiter = argonLimiter;
    }

    // Le niveau « Interactive » de Sodium.Core (32 Mio de mémoire, 4 passes) est prévu pour une connexion
    // en ligne. Il dépasse le minimum recommandé par l'OWASP pour Argon2id (19 Mio de mémoire, 2 passes).
    public async Task<string> HashAsync(string password)
    {
        return await argonLimiter.RunAsync(() =>
            PasswordHash.ArgonHashString(password, PasswordHash.StrengthArgon.Interactive));
    }

    // Renvoie vrai si le mot de passe correspond à l'empreinte enregistrée en base.
    public async Task<bool> VerifyAsync(string password, string storedHash)
    {
        return await argonLimiter.RunAsync(() =>
            PasswordHash.ArgonHashStringVerify(storedHash, password));
    }
}
