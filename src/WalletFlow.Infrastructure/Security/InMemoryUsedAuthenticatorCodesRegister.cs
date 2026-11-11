using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Infrastructure.Security;

// Se souvient en mémoire du dernier code utilisé par chaque compte : un code ne sert qu'une seule fois (SS §2).
// Si l'API redémarre, un code déjà utilisé peut resservir pendant le peu de temps où il reste valable.
public class InMemoryUsedAuthenticatorCodesRegister : IUsedAuthenticatorCodesRegister
{
    // Un code reste valable 90 secondes au plus après le moment où il a servi : 2 minutes de mémoire suffisent donc.
    public static readonly TimeSpan UsedCodeRetention = TimeSpan.FromMinutes(2);

    private readonly TimeProvider clock;
    private readonly Dictionary<Guid, UsedCode> usedCodesByAccountId = new Dictionary<Guid, UsedCode>();
    private readonly object usedCodesLock = new object();

    // Le nombre de codes gardés en mémoire : il montre que cette mémoire ne grossit pas sans fin.
    public int UsedCodeCount
    {
        get
        {
            lock (usedCodesLock)
            {
                return usedCodesByAccountId.Count;
            }
        }
    }

    // Reçoit l'horloge qui mesure la durée de cette mémoire.
    public InMemoryUsedAuthenticatorCodesRegister(TimeProvider clock)
    {
        this.clock = clock;
    }

    // Tout se passe sous le verrou : de deux requêtes simultanées avec le même code, une seule peut réussir.
    public bool TryRecordUse(Guid accountId, long period)
    {
        var now = clock.GetUtcNow();

        lock (usedCodesLock)
        {
            RemoveExpired(now);

            if (usedCodesByAccountId.TryGetValue(accountId, out var lastUsedCode)
                && lastUsedCode.Period >= period)
            {
                return false;
            }

            usedCodesByAccountId[accountId] = new UsedCode
            {
                Period = period,
                UsedAt = now
            };
            return true;
        }
    }

    // Oublie les codes trop vieux pour être encore valables, pour que la mémoire ne grossisse pas.
    private void RemoveExpired(DateTimeOffset now)
    {
        var expiredAccountIds = new List<Guid>();

        foreach (var entry in usedCodesByAccountId)
        {
            if (now - entry.Value.UsedAt >= UsedCodeRetention)
            {
                expiredAccountIds.Add(entry.Key);
            }
        }

        foreach (var accountId in expiredAccountIds)
        {
            usedCodesByAccountId.Remove(accountId);
        }
    }

    // Le dernier code utilisé par un compte.
    private class UsedCode
    {
        public required long Period { get; init; }
        public required DateTimeOffset UsedAt { get; init; }
    }
}
