using WalletFlow.Application.Administration;
using WalletFlow.Application.Support;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests de l'administration : lit et écrit les demandes et les réponses gardées par le faux dépôt
// du support, les comptes qu'il partage, et les connexions du faux dépôt des banques. Ce qui est préparé n'est gardé qu'avec les
// autres changements (voir FakeDatabaseUnitOfWork). Une demande lue a une version, comme xmin dans la vraie base : une autre
// requête simulée qui l'écrit avant l'enregistrement fait échouer celui-ci. Les vraies requêtes SQL sont testées sur la vraie
// base (AdministrationRepositoryTests). Chaque vue (voir CreateView) a ses propres changements en attente, comme un DbContext, sur la même fausse base.
public class FakeAdministrationRepository : IAdministrationRepository, ITransactionalFakeRepository
{
    public const string FailureDetail = "detail-technique-de-la-panne-de-l-administration";

    private readonly Store store;
    private readonly object pendingChangesLock;

    // La fausse base, commune à toutes les vues : la panne et les autres requêtes simulées.
    private sealed class Store
    {
        public object Gate { get; } = new object();

        public Guid? RequestResolvedBeforeSave { get; set; } = null;

        public int Conflicts { get; set; } = 0;

        public bool IsDown { get; set; } = false;
    }

    private readonly FakeSupportRequestRepository support;
    private readonly FakeBankConnectionRepository? bank;
    private readonly List<SupportResponse> preparedResponses = new List<SupportResponse>();
    private readonly List<SupportRequest> checkedRequests = new List<SupportRequest>();
    private readonly ReadCopies<SupportRequest> readRequests = new ReadCopies<SupportRequest>();
    private Action? concurrentChanges = null;

    // Reçoit le faux dépôt du support, qui garde les demandes, les réponses et les comptes, et celui des banques s'il y en a un.
    public FakeAdministrationRepository(FakeSupportRequestRepository support, FakeBankConnectionRepository? bank = null)
        : this(new Store(), support, bank)
    {
    }

    // Crée une vue sur cette fausse base.
    private FakeAdministrationRepository(Store store, FakeSupportRequestRepository support, FakeBankConnectionRepository? bank)
    {
        this.store = store;
        pendingChangesLock = store.Gate;
        this.support = support;
        this.bank = bank;
    }

    // Crée une autre vue sur la même fausse base, avec ses propres changements en attente.
    public FakeAdministrationRepository CreateView()
    {
        return new FakeAdministrationRepository(store, support, bank);
    }

    // Simule une autre requête qui marque cette demande « résolue » juste avant le prochain enregistrement de l'administrateur,
    // comme la vraie base : si cet enregistrement réécrit la ligne de la demande, il échoue, et le passage suivant relit la
    // demande résolue. Sinon, rien ne l'arrête : il passe, et la demande reste « résolue ».
    public Guid? RequestResolvedBeforeSave
    {
        get => store.RequestResolvedBeforeSave;
        set => store.RequestResolvedBeforeSave = value;
    }

    // Simule d'autres requêtes qui écrivent les mêmes lignes juste avant chaque enregistrement : un conflit chacune.
    public int Conflicts
    {
        get => store.Conflicts;
        set => store.Conflicts = value;
    }

    // Simule une panne de la base : lire ou écrire pour l'administrateur échoue.
    public bool IsDown
    {
        get => store.IsDown;
        set => store.IsDown = value;
    }

    // Donne les demandes gardées dans cet état, la plus ancienne d'abord, comme la vraie requête.
    public Task<List<AdminSupportRequestItem>> FindRequestsAsync(SupportRequestStatus status, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var items = support.SavedRequests
                .Where(request => request.Status == status)
                .OrderBy(request => request.SentAt)
                .ThenBy(request => request.Id)
                .Select(request => new AdminSupportRequestItem(
                    request.Id,
                    request.SentAt,
                    request.Status,
                    FindReplyAddress(request),
                    request.UserId,
                    support.SavedResponses.Count(response => response.SupportRequestId == request.Id)))
                .ToList();

            return Task.FromResult(items);
        }
    }

    // Donne la demande gardée avec ses réponses, de la plus ancienne à la plus récente, comme la vraie requête.
    public Task<AdminSupportRequestDetails?> FindRequestDetailsAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var request = support.SavedRequests.SingleOrDefault(saved => saved.Id == requestId);

            if (request == null)
            {
                return Task.FromResult<AdminSupportRequestDetails?>(null);
            }

            var responses = support.SavedResponses
                .Where(response => response.SupportRequestId == requestId)
                .OrderBy(response => response.SentAt)
                .ThenBy(response => response.Id)
                .Select(response => new SupportResponseView(response.Content, response.SentAt))
                .ToList();

            return Task.FromResult<AdminSupportRequestDetails?>(new AdminSupportRequestDetails(
                request.Id,
                request.SentAt,
                request.Status,
                FindReplyAddress(request),
                request.UserId,
                request.Message,
                responses));
        }
    }

    // Cherche la demande gardée, et garde une copie de ses valeurs, comme le suivi d'EF Core.
    public Task<SupportRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var request = support.SavedRequests.SingleOrDefault(saved => saved.Id == requestId);

            if (request != null)
            {
                readRequests.Keep(request);
            }

            return Task.FromResult(request);
        }
    }

    // Note la demande dont la ligne sera réécrite.
    public void PrepareRequestCheck(SupportRequest request)
    {
        lock (pendingChangesLock)
        {
            checkedRequests.Add(request);
        }
    }

    // Garde la réponse de côté : elle ne sera gardée qu'avec les autres changements.
    public void PrepareResponse(SupportResponse response)
    {
        lock (pendingChangesLock)
        {
            preparedResponses.Add(response);
        }
    }

    // Compte les comptes par état avec la règle du domaine, les connexions actives du faux dépôt des banques, et les demandes par état.
    public Task<AdministrationStatistics> CountStatisticsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var states = support.SavedUsers.Select(user => AccountStateRules.FindState(user, now)).ToList();
            var connections = bank?.SavedConnections ?? new List<BankConnection>();

            return Task.FromResult(new AdministrationStatistics(
                states.Count(state => state == AccountState.NotConfirmed),
                states.Count(state => state == AccountState.Active),
                states.Count(state => state == AccountState.Locked),
                states.Count(state => state == AccountState.BeingDeleted),
                connections.Count(connection => connection.Status == BankConnectionStatus.Active),
                support.SavedRequests.Count(request => request.Status == SupportRequestStatus.Sent),
                support.SavedRequests.Count(request => request.Status == SupportRequestStatus.InProgress),
                support.SavedRequests.Count(request => request.Status == SupportRequestStatus.Resolved)));
        }
    }

    // La panne touche tout ce que l'enregistrement écrit pour l'administrateur.
    public void ThrowIfTableIsDown()
    {
        lock (pendingChangesLock)
        {
            if (IsDown && IsWriting())
            {
                throw new InvalidOperationException(FailureDetail);
            }
        }
    }

    // Décide si une autre requête écrit les mêmes lignes juste avant l'enregistrement, selon les réglages du test.
    public bool HasConcurrentChange()
    {
        lock (pendingChangesLock)
        {
            if (RequestResolvedBeforeSave is Guid resolvedId && IsWriting())
            {
                RequestResolvedBeforeSave = null;
                Action resolution = () =>
                {
                    var request = support.SavedRequests.Single(saved => saved.Id == resolvedId);
                    EntityValues.WriteProperty(request, nameof(SupportRequest.Status), SupportRequestStatus.Resolved);
                };

                if (IsRequestWritten())
                {
                    concurrentChanges = resolution;
                    return true;
                }

                resolution();
            }

            if (Conflicts > 0 && IsWriting())
            {
                Conflicts = Conflicts - 1;
                return true;
            }

            return false;
        }
    }

    // Remet les demandes lues comme elles étaient, et oublie ce qui a été préparé.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readRequests.RestoreAll();
            ClearTracking();
        }
    }

    // Écrit ce que l'autre requête simulée a enregistré.
    public void ApplyConcurrentChanges()
    {
        Action? changes;

        lock (pendingChangesLock)
        {
            changes = concurrentChanges;
            concurrentChanges = null;
        }

        if (changes != null)
        {
            lock (pendingChangesLock)
            {
                changes();
            }
        }
    }

    // Garde les réponses préparées. Les demandes changées le sont déjà : ce sont les objets gardés.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            support.SavedResponses.AddRange(preparedResponses);
            ClearTracking();
        }
    }

    // L'adresse où répondre : celle du compte gardé, ou celle du visiteur.
    private string FindReplyAddress(SupportRequest request)
    {
        return support.SavedUsers.SingleOrDefault(user => user.Id == request.UserId)?.Email ?? request.ContactEmail!;
    }

    // Vrai si l'enregistrement écrit la ligne d'une demande : contrôlée, ou changée depuis sa lecture.
    private bool IsRequestWritten()
    {
        return checkedRequests.Count > 0 || readRequests.FindChangedSinceRead((copy, current) => copy.Status != current.Status) != null;
    }

    // Vrai si l'enregistrement écrit quelque chose pour l'administrateur.
    private bool IsWriting()
    {
        return preparedResponses.Count > 0 || IsRequestWritten();
    }

    // Lance la panne simulée.
    private void ThrowIfDown()
    {
        if (IsDown)
        {
            throw new InvalidOperationException(FailureDetail);
        }
    }

    // Oublie ce qui a été lu et préparé depuis le dernier enregistrement.
    private void ClearTracking()
    {
        preparedResponses.Clear();
        checkedRequests.Clear();
        readRequests.Clear();
    }
}
