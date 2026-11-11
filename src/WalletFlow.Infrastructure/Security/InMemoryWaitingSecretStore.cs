using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Security;

// Garde en mémoire les secrets qui attendent le premier code, 10 minutes au plus : le MPD interdit de les écrire en base avant l'activation.
// Ils disparaissent si l'API redémarre : il suffit alors de recommencer l'activation.
public class InMemoryWaitingSecretStore : IWaitingSecretStore
{
    private readonly TimeProvider clock;
    private readonly Dictionary<Guid, StoredSecret> secretsByAccountId = new Dictionary<Guid, StoredSecret>();
    private readonly object waitingSecretsLock = new object();

    // Le nombre de secrets gardés en mémoire : il montre que cette mémoire ne grossit pas sans fin.
    public int WaitingSecretCount
    {
        get
        {
            lock (waitingSecretsLock)
            {
                return secretsByAccountId.Count;
            }
        }
    }

    // Reçoit l'horloge qui mesure la durée de chaque attente.
    public InMemoryWaitingSecretStore(TimeProvider clock)
    {
        this.clock = clock;
    }

    // Garde le secret de ce compte, et oublie ceux qui ont expiré, pour que la mémoire ne grossisse pas.
    public void Save(Guid accountId, string secret)
    {
        var now = clock.GetUtcNow();

        lock (waitingSecretsLock)
        {
            RemoveExpired(now);

            secretsByAccountId[accountId] = new StoredSecret
            {
                Secret = secret,
                ExpiresAt = now + TwoFactorAuthenticationRules.WaitingSecretLifetime
            };
        }
    }

    // Donne le secret de ce compte. Renvoie null s'il n'y en a pas, ou s'il a expiré.
    public string? Find(Guid accountId)
    {
        var now = clock.GetUtcNow();

        lock (waitingSecretsLock)
        {
            if (!secretsByAccountId.TryGetValue(accountId, out var storedSecret)
                || storedSecret.ExpiresAt <= now)
            {
                return null;
            }

            return storedSecret.Secret;
        }
    }

    // Oublie le secret de ce compte. Ne fait rien s'il n'y en a plus.
    public void Remove(Guid accountId)
    {
        lock (waitingSecretsLock)
        {
            secretsByAccountId.Remove(accountId);
        }
    }

    // Oublie les secrets dont l'attente est finie.
    private void RemoveExpired(DateTimeOffset now)
    {
        var expiredAccountIds = new List<Guid>();

        foreach (var entry in secretsByAccountId)
        {
            if (entry.Value.ExpiresAt <= now)
            {
                expiredAccountIds.Add(entry.Key);
            }
        }

        foreach (var accountId in expiredAccountIds)
        {
            secretsByAccountId.Remove(accountId);
        }
    }

    // Un secret en attente, avec la fin de son attente.
    private class StoredSecret
    {
        public required string Secret { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
