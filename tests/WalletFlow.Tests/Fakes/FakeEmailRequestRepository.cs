using WalletFlow.Application.Emails;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde la file d'e-mails en mémoire. Une demande préparée n'entre dans la file
// qu'avec les autres changements (voir FakeDatabaseUnitOfWork), et réveille alors la tâche de fond, comme la vraie file.
public class FakeEmailRequestRepository : IEmailRequestRepository, ITransactionalFakeRepository
{
    public const string FailureDetail = "detail-technique-de-la-panne-de-la-file";

    private readonly object pendingChangesLock = new object();
    private readonly List<EmailRequest> preparedRequests = new List<EmailRequest>();
    private readonly List<EmailRequest> preparedRemovals = new List<EmailRequest>();
    private readonly ReadCopies<EmailRequest> readRequests = new ReadCopies<EmailRequest>();
    private Guid? requestToRemoveAfterRollback = null;

    // Reçoit le réveil de la tâche de fond, le même que celui qu'elle attend.
    public FakeEmailRequestRepository(EmailRequestWakeUp wakeUp)
    {
        WakeUp = wakeUp;
    }

    // Pour les tests sans tâche de fond : personne n'attend ce réveil.
    public FakeEmailRequestRepository()
        : this(new EmailRequestWakeUp())
    {
    }

    public EmailRequestWakeUp WakeUp { get; }

    // Ce que la fausse base contient.
    public List<EmailRequest> SavedRequests { get; } = new List<EmailRequest>();

    // Simule une panne de la base : lire la file ou y écrire échoue.
    public bool IsDown { get; set; } = false;

    // Simule une autre requête qui sort cette demande de la file juste avant l'enregistrement,
    // comme l'effacement de son compte : l'enregistrement échoue, et la demande n'est plus là.
    public Guid? RemovedBeforeSave { get; set; } = null;

    // Les demandes gardées en mémoire, lues même pendant qu'une tâche de fond écrit.
    public List<EmailRequest> ReadSavedRequests()
    {
        lock (pendingChangesLock)
        {
            return new List<EmailRequest>(SavedRequests);
        }
    }

    // Garde la demande de côté : elle n'entrera dans la file qu'avec les autres changements.
    public void PrepareRequest(EmailRequest request)
    {
        lock (pendingChangesLock)
        {
            preparedRequests.Add(request);
        }
    }

    // Cherche en mémoire la demande dont l'heure d'essai est passée depuis le plus longtemps.
    public Task<Guid?> FindNextDueIdAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var nextRequest = SavedRequests
                .Where(request => request.NextTryAt <= now)
                .OrderBy(request => request.NextTryAt)
                .FirstOrDefault();

            return Task.FromResult(nextRequest?.Id);
        }
    }

    // Cherche en mémoire une demande, et garde une copie de ses valeurs, comme le suivi des objets d'EF Core.
    public Task<EmailRequest?> FindAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var request = SavedRequests.SingleOrDefault(savedRequest => savedRequest.Id == requestId);
            if (request != null)
            {
                readRequests.Keep(request);
            }

            return Task.FromResult(request);
        }
    }

    // Garde de côté la sortie de la demande : elle ne se fera qu'avec les autres changements.
    public void PrepareRemoval(EmailRequest request)
    {
        lock (pendingChangesLock)
        {
            preparedRemovals.Add(request);
        }
    }

    // La panne touche tout ce que l'enregistrement écrit dans la file.
    public void ThrowIfTableIsDown()
    {
        lock (pendingChangesLock)
        {
            if (IsDown && (preparedRequests.Count > 0 || preparedRemovals.Count > 0 || FindRequestChangedSinceRead() != null))
            {
                throw new InvalidOperationException(FailureDetail);
            }
        }
    }

    // Une autre requête sort la demande de la file juste avant que cet enregistrement la touche, si le test le demande.
    public bool HasConcurrentChange()
    {
        lock (pendingChangesLock)
        {
            if (RemovedBeforeSave is not Guid removedId)
            {
                return false;
            }

            var touchesRemovedRequest = preparedRemovals.Any(request => request.Id == removedId)
                || FindRequestChangedSinceRead()?.Id == removedId;
            if (!touchesRemovedRequest)
            {
                return false;
            }

            RemovedBeforeSave = null;
            requestToRemoveAfterRollback = removedId;
            return true;
        }
    }

    // Oublie ce qui a été préparé, et remet les demandes lues comme elles étaient.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readRequests.RestoreAll();
            ClearTracking();
        }
    }

    // Écrit ce que l'autre requête simulée a fait : la demande n'est plus dans la file.
    public void ApplyConcurrentChanges()
    {
        lock (pendingChangesLock)
        {
            if (requestToRemoveAfterRollback is Guid removedId)
            {
                SavedRequests.RemoveAll(request => request.Id == removedId);
                requestToRemoveAfterRollback = null;
            }
        }
    }

    // Garde les nouvelles demandes et les sorties de la file, puis réveille la tâche de fond s'il y a du nouveau.
    public void Commit()
    {
        var hasNewRequests = false;

        lock (pendingChangesLock)
        {
            hasNewRequests = preparedRequests.Count > 0;
            SavedRequests.AddRange(preparedRequests);

            foreach (var removedRequest in preparedRemovals)
            {
                SavedRequests.Remove(removedRequest);
            }

            ClearTracking();
        }

        if (hasNewRequests)
        {
            WakeUp.Signal();
        }
    }

    // Lance la panne simulée.
    private void ThrowIfDown()
    {
        if (IsDown)
        {
            throw new InvalidOperationException(FailureDetail);
        }
    }

    // Donne une demande lue dont les essais ont changé depuis sa lecture, ou null.
    private EmailRequest? FindRequestChangedSinceRead()
    {
        return readRequests.FindChangedSinceRead((copy, request) =>
            copy.FailedTryCount != request.FailedTryCount || copy.NextTryAt != request.NextTryAt);
    }

    // Oublie ce qui a été préparé et les copies des demandes lues.
    private void ClearTracking()
    {
        preparedRequests.Clear();
        preparedRemovals.Clear();
        readRequests.Clear();
    }
}
