using WalletFlow.Application.Security;

namespace WalletFlow.Tests.Fakes;

// Remplace le hachage dans les tests : rapide et prévisible, et compte les mots de passe hachés.
public class FakePasswordHasher : IPasswordHasher
{
    public const string HashPrefix = "fausse-empreinte-de-";

    public int HashedPasswordCount { get; private set; } = 0;
    public int VerifiedPasswordCount { get; private set; } = 0;

    // Appelée à chaque comparaison : permet à un test de regarder l'état de la base à ce moment-là.
    public Action? OnPasswordVerified { get; set; } = null;

    // Renvoie une fausse empreinte, facile à reconnaître dans les tests.
    public Task<string> HashAsync(string password)
    {
        HashedPasswordCount = HashedPasswordCount + 1;
        return Task.FromResult(HashPrefix + password);
    }

    // Compare le mot de passe avec la fausse empreinte.
    public Task<bool> VerifyAsync(string password, string storedHash)
    {
        VerifiedPasswordCount = VerifiedPasswordCount + 1;
        OnPasswordVerified?.Invoke();
        return Task.FromResult(storedHash == HashPrefix + password);
    }
}
