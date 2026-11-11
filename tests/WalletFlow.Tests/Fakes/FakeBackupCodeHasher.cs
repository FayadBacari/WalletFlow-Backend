using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Tests.Fakes;

// Remplace le hachage des codes de secours dans les tests : rapide et prévisible, et compte les codes hachés.
public class FakeBackupCodeHasher : IBackupCodeHasher
{
    public const string HashPrefix = "fausse-empreinte-de-code-";

    public int HashedCodeCount { get; private set; } = 0;

    // Appelée à chaque code haché : permet à un test de changer la base pendant la vérification d'un code.
    public Action? OnBackupCodeHashed { get; set; } = null;

    // Donne la fausse empreinte de ce code pour ce compte : celle que calcule ce faux hachage.
    public static string CreateFakeHash(string cleanedCode, Guid accountId)
    {
        return HashPrefix + accountId + "-" + cleanedCode;
    }

    // Renvoie la fausse empreinte du code, et compte le calcul.
    public Task<string> HashAsync(string cleanedCode, Guid accountId)
    {
        HashedCodeCount = HashedCodeCount + 1;
        OnBackupCodeHashed?.Invoke();
        return Task.FromResult(CreateFakeHash(cleanedCode, accountId));
    }
}
