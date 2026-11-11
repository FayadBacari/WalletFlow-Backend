using WalletFlow.Application.Security;

namespace WalletFlow.Infrastructure.Security;

// Compte en mémoire les demandes par adresse e-mail : 3 par heure (SS §6).
// Les compteurs repartent à zéro si l'API redémarre, comme les limites par adresse IP.
public class InMemoryEmailAddressRequestLimiter : IEmailAddressRequestLimiter
{
    public const int MaximumRequestsPerAddress = 3;
    public static readonly TimeSpan CountingWindow = TimeSpan.FromHours(1);

    private readonly TimeProvider clock;
    private readonly Dictionary<string, RequestCounter> countersByAddress = new Dictionary<string, RequestCounter>();
    private readonly object countersLock = new object();

    // Reçoit l'horloge qui mesure la durée de chaque compteur.
    public InMemoryEmailAddressRequestLimiter(TimeProvider clock)
    {
        this.clock = clock;
    }

    // Compte une demande de plus pour cette adresse. Renvoie faux si la limite est déjà atteinte.
    public bool TryCountRequest(string email)
    {
        var now = clock.GetUtcNow();

        lock (countersLock)
        {
            RemoveExpiredCounters(now);

            if (!countersByAddress.TryGetValue(email, out var counter))
            {
                countersByAddress[email] = new RequestCounter
                {
                    FirstRequestAt = now,
                    RequestCount = 1
                };
                return true;
            }

            if (counter.RequestCount >= MaximumRequestsPerAddress)
            {
                return false;
            }

            counter.RequestCount = counter.RequestCount + 1;
            return true;
        }
    }

    // Oublie les adresses dont la période de comptage est finie, pour que la mémoire ne grossisse pas.
    private void RemoveExpiredCounters(DateTimeOffset now)
    {
        var expiredAddresses = new List<string>();

        foreach (var entry in countersByAddress)
        {
            if (now - entry.Value.FirstRequestAt >= CountingWindow)
            {
                expiredAddresses.Add(entry.Key);
            }
        }

        foreach (var email in expiredAddresses)
        {
            countersByAddress.Remove(email);
        }
    }

    // Compteur d'une adresse e-mail pendant sa période de comptage.
    private class RequestCounter
    {
        public DateTimeOffset FirstRequestAt { get; init; }
        public int RequestCount { get; set; }
    }
}
