using WalletFlow.Application.Security;

namespace WalletFlow.Tests.Fakes;

// Remplace Pwned Passwords dans les tests : aucun test n'interroge le vrai service. Un mot de passe est divulgué s'il est
// dans la liste du test.
public class FakeBreachedPasswordChecker : IBreachedPasswordChecker
{
    private readonly object passwordsLock = new object();
    private readonly HashSet<string> breachedPasswords = new HashSet<string>(StringComparer.Ordinal);
    private int checkCount = 0;

    // Le nombre de mots de passe cherchés : il montre qu'un contrôle a bien eu lieu, ou qu'il n'a pas eu lieu.
    public int CheckCount => Volatile.Read(ref checkCount);

    // Ajoute ce mot de passe, déjà normalisé, aux fuites connues.
    public void AddBreachedPassword(string normalizedPassword)
    {
        lock (passwordsLock)
        {
            breachedPasswords.Add(normalizedPassword);
        }
    }

    // Cherche le mot de passe dans la liste du test.
    public Task<bool> IsBreachedAsync(string normalizedPassword, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref checkCount);

        lock (passwordsLock)
        {
            return Task.FromResult(breachedPasswords.Contains(normalizedPassword));
        }
    }
}
