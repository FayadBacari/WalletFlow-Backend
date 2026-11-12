using WalletFlow.Application.BankConnections;

namespace WalletFlow.Infrastructure.BankData;

// Compte en mémoire les mises à jour demandées par chaque utilisateur : une par minute (SS §6).
// Les compteurs repartent à zéro si l'API redémarre, comme les autres limites.
public class InMemoryBankUpdateLimiter : IBankUpdateLimiter
{
    public static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(1);

    private readonly TimeProvider clock;
    private readonly Dictionary<Guid, DateTimeOffset> lastUpdateByUser = new Dictionary<Guid, DateTimeOffset>();
    private readonly object updatesLock = new object();

    // Reçoit l'horloge qui mesure la minute.
    public InMemoryBankUpdateLimiter(TimeProvider clock)
    {
        this.clock = clock;
    }

    // Compte une mise à jour si la dernière de cet utilisateur date d'une minute ou plus.
    public bool TryCountUpdate(Guid userId)
    {
        var now = clock.GetUtcNow();

        lock (updatesLock)
        {
            RemoveFinishedIntervals(now);

            if (lastUpdateByUser.ContainsKey(userId))
            {
                return false;
            }

            lastUpdateByUser[userId] = now;
            return true;
        }
    }

    // Oublie les utilisateurs dont la minute est finie, pour que la mémoire ne grossisse pas.
    private void RemoveFinishedIntervals(DateTimeOffset now)
    {
        var finishedUsers = lastUpdateByUser
            .Where(entry => now - entry.Value >= UpdateInterval)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var userId in finishedUsers)
        {
            lastUpdateByUser.Remove(userId);
        }
    }
}
