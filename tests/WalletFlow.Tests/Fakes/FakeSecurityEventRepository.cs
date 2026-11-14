using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde le journal de sécurité en mémoire.
public class FakeSecurityEventRepository : ISecurityEventRepository, ITransactionalFakeRepository
{
    public const string FailureDetail = "detail-technique-de-la-panne-du-journal";

    private readonly object pendingChangesLock = new object();
    private readonly List<SecurityEvent> preparedEvents = new List<SecurityEvent>();

    // Ce que la fausse base contient.
    public List<SecurityEvent> SavedSecurityEvents { get; } = new List<SecurityEvent>();

    // Simule une panne de la base au moment d'écrire dans le journal.
    public bool IsDown { get; set; } = false;

    // Le nombre d'effacements des événements de plus d'un an demandés. La vraie durée est testée sur la vraie base.
    public int CleanupCount { get; private set; } = 0;

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
    public Task<int> DeleteExpiredEventsAsync()
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
