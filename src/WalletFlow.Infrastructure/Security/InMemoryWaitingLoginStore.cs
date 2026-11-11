using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Security;

// Garde en mémoire les connexions qui attendent leur code, 5 minutes au plus (SS §2).
// Elles disparaissent si l'API redémarre : il suffit alors de se reconnecter.
public class InMemoryWaitingLoginStore : IWaitingLoginStore
{
    private readonly TimeProvider clock;
    private readonly Dictionary<string, StoredLogin> loginsByTokenHash = new Dictionary<string, StoredLogin>();
    private readonly object waitingLoginsLock = new object();

    // Le nombre de connexions gardées en mémoire : il montre que cette mémoire ne grossit pas sans fin.
    public int WaitingLoginCount
    {
        get
        {
            lock (waitingLoginsLock)
            {
                return loginsByTokenHash.Count;
            }
        }
    }

    // Reçoit l'horloge qui mesure la durée de chaque attente.
    public InMemoryWaitingLoginStore(TimeProvider clock)
    {
        this.clock = clock;
    }

    // Garde une nouvelle connexion en attente, et oublie celles qui ont expiré, pour que la mémoire ne grossisse pas.
    public void Save(string tokenHash, Guid accountId, string passwordHash)
    {
        var now = clock.GetUtcNow();

        lock (waitingLoginsLock)
        {
            RemoveExpired(now);

            loginsByTokenHash[tokenHash] = new StoredLogin
            {
                AccountId = accountId,
                PasswordHash = passwordHash,
                ExpiresAt = now + TwoFactorAuthenticationRules.WaitingLoginLifetime
            };
        }
    }

    // Compte un essai de plus, sous le verrou : chaque requête simultanée reçoit donc son propre numéro d'essai.
    public WaitingLogin? TryCountCodeAttempt(string tokenHash)
    {
        var now = clock.GetUtcNow();

        lock (waitingLoginsLock)
        {
            if (!loginsByTokenHash.TryGetValue(tokenHash, out var storedLogin))
            {
                return null;
            }

            if (storedLogin.ExpiresAt <= now
                || storedLogin.TriedCodeCount >= TwoFactorAuthenticationRules.MaximumCodesPerLogin)
            {
                return null;
            }

            storedLogin.TriedCodeCount = storedLogin.TriedCodeCount + 1;

            return new WaitingLogin
            {
                AccountId = storedLogin.AccountId,
                PasswordHash = storedLogin.PasswordHash,
                TriedCodeCount = storedLogin.TriedCodeCount
            };
        }
    }

    // Oublie une connexion en attente. Ne fait rien si elle n'existe plus.
    public void Remove(string tokenHash)
    {
        lock (waitingLoginsLock)
        {
            loginsByTokenHash.Remove(tokenHash);
        }
    }

    // Oublie les connexions dont l'attente est finie.
    private void RemoveExpired(DateTimeOffset now)
    {
        var expiredTokenHashes = new List<string>();

        foreach (var entry in loginsByTokenHash)
        {
            if (entry.Value.ExpiresAt <= now)
            {
                expiredTokenHashes.Add(entry.Key);
            }
        }

        foreach (var tokenHash in expiredTokenHashes)
        {
            loginsByTokenHash.Remove(tokenHash);
        }
    }

    // Une connexion en attente, avec son nombre d'essais de code et la fin de son attente.
    private class StoredLogin
    {
        public required Guid AccountId { get; init; }
        public required string PasswordHash { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public int TriedCodeCount { get; set; }
    }
}
