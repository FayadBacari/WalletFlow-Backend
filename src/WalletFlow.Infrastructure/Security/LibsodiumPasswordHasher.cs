using Sodium;
using WalletFlow.Application.Security;

namespace WalletFlow.Infrastructure.Security;

// Hache les mots de passe en Argon2id avec libsodium, une bibliothèque reconnue (SS §2 et §13).
// L'empreinte contient son sel et ses réglages : deux utilisateurs avec le même mot de passe
// ont donc deux empreintes différentes.
public class LibsodiumPasswordHasher : IPasswordHasher
{
    // Le niveau « Interactive » de Sodium.Core (32 Mio de mémoire, 4 passes) est prévu pour une connexion
    // en ligne. Il dépasse le minimum recommandé par l'OWASP pour Argon2id (19 Mio de mémoire, 2 passes).
    public string ConvertPasswordIntoItsSecureHash(string passwordToConvertIntoSecureHash)
    {
        return PasswordHash.ArgonHashString(passwordToConvertIntoSecureHash, PasswordHash.StrengthArgon.Interactive);
    }

    // Renvoie vrai si le mot de passe correspond à l'empreinte enregistrée en base.
    public bool CheckThatPasswordMatchesItsSecureHash(string passwordToCompareWithItsHash, string secureHashStoredInTheDatabase)
    {
        return PasswordHash.ArgonHashStringVerify(secureHashStoredInTheDatabase, passwordToCompareWithItsHash);
    }
}
