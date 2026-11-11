using WalletFlow.Application.Security;

namespace WalletFlow.Tests.Fakes;

// Remplace la création des jetons dans les tests : renvoie toujours le même jeton, facile à reconnaître.
public class FakeSecureTokenGenerator : ISecureTokenGenerator
{
    public const string FixedToken = "faux-jeton-du-lien";
    public const string FixedHash = "fausse-empreinte-du-jeton";

    // Renvoie toujours le même jeton et la même empreinte.
    public GeneratedSecureToken GenerateToken()
    {
        return new GeneratedSecureToken(FixedToken, FixedHash);
    }

    // Donne l'empreinte du faux jeton, ou une autre empreinte pour tout autre jeton.
    public string HashToken(string token)
    {
        if (token == FixedToken)
        {
            return FixedHash;
        }

        return "autre-empreinte-de-" + token;
    }

    // Les tests de la double authentification utilisent le vrai générateur : chaque code de secours doit être différent.
    public string GenerateBackupCode()
    {
        throw new NotSupportedException("Ce faux générateur ne tire pas de code de secours.");
    }
}
