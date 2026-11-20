using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde le journal de sécurité en mémoire. Chaque vue (voir CreateView) a ses propres
// événements préparés, comme un DbContext, sur la même fausse base.
public class FakeSecurityEventRepository : ISecurityEventRepository, ITransactionalFakeRepository
{
    public const string FailureDetail = "detail-technique-de-la-panne-du-journal";

    private readonly Store store;
    private readonly object pendingChangesLock;
    private readonly List<SecurityEvent> preparedEvents = new List<SecurityEvent>();

    // La fausse base, commune à toutes les vues : ce qui est enregistré, la panne simulée et le compteur.
    private sealed class Store
    {
        public object Gate { get; } = new object();

        public List<SecurityEvent> SavedSecurityEvents { get; } = new List<SecurityEvent>();

        public bool IsDown { get; set; } = false;

        public int CleanupCount { get; set; } = 0;
    }

    // Crée une fausse base vide, et sa première vue.
    public FakeSecurityEventRepository()
        : this(new Store())
    {
    }

    // Crée une vue sur cette fausse base.
    private FakeSecurityEventRepository(Store store)
    {
        this.store = store;
        pendingChangesLock = store.Gate;
    }

    // Ce que la fausse base contient.
    public List<SecurityEvent> SavedSecurityEvents => store.SavedSecurityEvents;

    // Simule une panne de la base au moment d'écrire dans le journal.
    public bool IsDown
    {
        get => store.IsDown;
        set => store.IsDown = value;
    }

    // Le nombre d'effacements des événements de plus d'un an demandés. La vraie durée est testée sur la vraie base.
    public int CleanupCount
    {
        get => store.CleanupCount;
        private set => store.CleanupCount = value;
    }

    // Crée une autre vue sur la même fausse base, avec ses propres événements préparés.
    public FakeSecurityEventRepository CreateView()
    {
        return new FakeSecurityEventRepository(store);
    }

    // Garde tout de suite l'événement en mémoire.
    public Task SaveEventAsync(SecurityEvent securityEvent)
    {
        if (IsDown)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        lock (pendingChangesLock)
        {
            SavedSecurityEvents.Add(securityEvent);
        }

        return Task.CompletedTask;
    }

    // Note l'effacement demandé, ou lance la panne simulée.
    // Donne la date du dernier événement réussi de ce type pour ce compte, comme la vraie requête.
    public Task<DateTime?> FindLastSuccessAsync(SecurityEventType type, Guid accountId, CancellationToken cancellationToken = default)
    {
        if (IsDown)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        lock (pendingChangesLock)
        {
            var times = SavedSecurityEvents
                .Where(securityEvent => securityEvent.UserId == accountId && securityEvent.Type == type && securityEvent.Result == SecurityEventResult.Success)
                .Select(securityEvent => (DateTime?)securityEvent.OccurredAt)
                .ToList();

            return Task.FromResult(times.Max());
        }
    }

    public Task<int> DeleteExpiredEventsAsync(CancellationToken cancellationToken = default)
    {
        if (IsDown)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        lock (pendingChangesLock)
        {
            CleanupCount = CleanupCount + 1;
        }

        return Task.FromResult(0);
    }

    // Garde l'événement de côté : il ne sera gardé qu'avec les autres changements.
    public void PrepareEvent(SecurityEvent securityEvent)
    {
        lock (pendingChangesLock)
        {
            preparedEvents.Add(securityEvent);
        }
    }

    // La panne du journal touche aussi les événements préparés.
    public void ThrowIfTableIsDown()
    {
        lock (pendingChangesLock)
        {
            if (IsDown && preparedEvents.Count > 0)
            {
                throw new InvalidOperationException(FailureDetail);
            }
        }
    }

    // Le journal n'a pas de conflit simulé : chaque événement est une nouvelle ligne.
    public bool HasConcurrentChange()
    {
        return false;
    }

    // Oublie les événements préparés.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            preparedEvents.Clear();
        }
    }

    // Aucune autre requête n'est simulée sur le journal.
    public void ApplyConcurrentChanges()
    {
    }

    // Garde les événements préparés.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            SavedSecurityEvents.AddRange(preparedEvents);
            preparedEvents.Clear();
        }
    }
}
