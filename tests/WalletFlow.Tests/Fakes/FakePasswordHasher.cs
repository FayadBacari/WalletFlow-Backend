using WalletFlow.Application.Security;

namespace WalletFlow.Tests.Fakes;

// Remplace le hachage dans les tests : rapide et prévisible, et compte les mots de passe hachés.
public class FakePasswordHasher : IPasswordHasher
{
    public const string PrefixOfTheFakePasswordHashes = "fausse-empreinte-de-";

    public int NumberOfPasswordsHashed { get; private set; } = 0;
    public int NumberOfPasswordsCompared { get; private set; } = 0;

    // Appelée à chaque comparaison : permet à un test de regarder l'état de la base à ce moment-là.
    public Action? ActionRunWhenOnePasswordIsCompared { get; set; } = null;

    // Renvoie une fausse empreinte, facile à reconnaître dans les tests.
    public string ConvertPasswordIntoItsSecureHash(string passwordToConvertIntoSecureHash)
    {
        NumberOfPasswordsHashed = NumberOfPasswordsHashed + 1;
        return PrefixOfTheFakePasswordHashes + passwordToConvertIntoSecureHash;
    }

    // Compare le mot de passe avec la fausse empreinte.
    public bool CheckThatPasswordMatchesItsSecureHash(string passwordToCompareWithItsHash, string secureHashStoredInTheDatabase)
    {
        NumberOfPasswordsCompared = NumberOfPasswordsCompared + 1;
        ActionRunWhenOnePasswordIsCompared?.Invoke();

        return secureHashStoredInTheDatabase == PrefixOfTheFakePasswordHashes + passwordToCompareWithItsHash;
    }
}
