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
}
