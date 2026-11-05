using WalletFlow.Application.Security;

namespace WalletFlow.Tests.Fakes;

// Remplace la création des jetons dans les tests : renvoie toujours le même jeton, facile à reconnaître.
public class FakeSecureTokenGenerator : ISecureTokenGenerator
{
    public const string TokenAlwaysReturnedByThisFakeGenerator = "faux-jeton-du-lien";
    public const string HashAlwaysReturnedByThisFakeGenerator = "fausse-empreinte-du-jeton";

    // Renvoie toujours le même jeton et la même empreinte.
    public GeneratedSecureToken GenerateNewRandomSecureTokenWithItsHash()
    {
        return new GeneratedSecureToken(TokenAlwaysReturnedByThisFakeGenerator, HashAlwaysReturnedByThisFakeGenerator);
    }

    // Donne l'empreinte du faux jeton, ou une autre empreinte pour tout autre jeton.
    public string ConvertSecureTokenIntoItsHash(string tokenToConvertIntoItsHash)
    {
        if (tokenToConvertIntoItsHash == TokenAlwaysReturnedByThisFakeGenerator)
        {
            return HashAlwaysReturnedByThisFakeGenerator;
        }

        return "autre-empreinte-de-" + tokenToConvertIntoItsHash;
    }

    // Les tests de la double authentification utilisent le vrai générateur : chaque code de secours doit être différent.
    public string GenerateNewRandomBackupCodeInItsCleanForm()
    {
        throw new NotSupportedException("Ce faux générateur ne tire pas de code de secours.");
    }
}
