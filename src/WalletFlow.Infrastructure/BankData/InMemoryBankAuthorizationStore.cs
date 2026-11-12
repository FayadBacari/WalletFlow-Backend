using WalletFlow.Application.BankConnections;

namespace WalletFlow.Infrastructure.BankData;

// Garde en mémoire les autorisations commencées, 15 minutes au plus, et 5 au plus par utilisateur (SS §7).
// Elles disparaissent si l'API redémarre : l'utilisateur n'a plus qu'à recommencer depuis WalletFlow.
public class InMemoryBankAuthorizationStore : IBankAuthorizationStore
{
    public static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(15);
    public const int MaximumPendingPerUser = 5;

    private readonly TimeProvider clock;
    private readonly Dictionary<string, StoredAuthorization> authorizationsByStateHash = new Dictionary<string, StoredAuthorization>();
    private readonly object authorizationsLock = new object();

    // Le nombre d'autorisations gardées en mémoire : il montre que cette mémoire ne grossit pas sans fin.
    public int PendingCount
    {
        get
        {
            lock (authorizationsLock)
            {
                return authorizationsByStateHash.Count;
            }
        }
    }

    // Reçoit l'horloge qui mesure la durée de chaque attente.
    public InMemoryBankAuthorizationStore(TimeProvider clock)
    {
        this.clock = clock;
    }

    // Garde une autorisation commencée. Les autorisations expirées sont oubliées, et la plus ancienne de l'utilisateur
    // aussi s'il en a déjà 5 : un utilisateur ne peut pas remplir la mémoire.
    public void Save(string stateHash, PendingBankAuthorization authorization)
    {
        var now = clock.GetUtcNow();

        lock (authorizationsLock)
        {
            RemoveExpired(now);
            RemoveOldestIfFull(authorization.UserId);

            authorizationsByStateHash[stateHash] = new StoredAuthorization(authorization, now, now + AuthorizationLifetime);
        }
    }

    // Reprend l'autorisation une seule fois : elle est oubliée dès qu'on la demande, même si elle est expirée ou à un autre utilisateur.
    public PendingBankAuthorization? TryTake(string stateHash, Guid userId)
    {
        var now = clock.GetUtcNow();

        lock (authorizationsLock)
        {
            if (!authorizationsByStateHash.Remove(stateHash, out var stored))
            {
                return null;
            }

            if (stored.ExpiresAt <= now || stored.Authorization.UserId != userId)
            {
                return null;
            }

            return stored.Authorization;
        }
    }

    // Oublie les autorisations dont l'attente est finie.
    private void RemoveExpired(DateTimeOffset now)
    {
        var expiredStateHashes = authorizationsByStateHash
            .Where(entry => entry.Value.ExpiresAt <= now)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var stateHash in expiredStateHashes)
        {
            authorizationsByStateHash.Remove(stateHash);
        }
    }

    // Oublie la plus ancienne autorisation de cet utilisateur s'il en a déjà le nombre maximum.
    private void RemoveOldestIfFull(Guid userId)
    {
        var userEntries = authorizationsByStateHash
            .Where(entry => entry.Value.Authorization.UserId == userId)
            .OrderBy(entry => entry.Value.SavedAt)
            .ToList();

        if (userEntries.Count >= MaximumPendingPerUser)
        {
            authorizationsByStateHash.Remove(userEntries[0].Key);
        }
    }

    // Une autorisation gardée, avec l'heure où elle a été commencée et la fin de son attente.
    private sealed record StoredAuthorization(PendingBankAuthorization Authorization, DateTimeOffset SavedAt, DateTimeOffset ExpiresAt);
}
