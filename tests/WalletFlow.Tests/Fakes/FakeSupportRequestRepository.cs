using WalletFlow.Application.Support;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les demandes de support et leurs réponses en mémoire. Ce qui est préparé
// n'est gardé qu'avec les autres changements (voir FakeDatabaseUnitOfWork). La ligne de l'utilisateur a une version, comme
// xmin dans la vraie base : son contrôle échoue si une autre requête l'a écrite depuis sa lecture. Les vraies requêtes SQL
// sont testées sur la vraie base (SupportRequestRepositoryTests). Chaque vue (voir CreateView) a ses propres changements en attente, comme un DbContext, sur la même fausse base.
public class FakeSupportRequestRepository : ISupportRequestRepository, ITransactionalFakeRepository
{
    public const string FailureDetail = "detail-technique-de-la-panne-du-support";

    private readonly Store store;
    private readonly object pendingChangesLock;

    // La fausse base, commune à toutes les vues : ce qui est enregistré, la version de la ligne de l'utilisateur, ce que les tests vérifient, et la panne et les requêtes simulées.
    private sealed class Store
    {
        public object Gate { get; } = new object();

        public List<User> SavedUsers { get; set; } = new List<User>();

        public List<SupportRequest> SavedRequests { get; set; } = new List<SupportRequest>();

        public List<SupportResponse> SavedResponses { get; set; } = new List<SupportResponse>();

        public List<User> CommittedUserChecks { get; set; } = new List<User>();

        public SupportRequest? RequestSavedAfterCount { get; set; } = null;

        public int Conflicts { get; set; } = 0;

        public bool IsDown { get; set; } = false;

        public int UserRowVersion { get; set; } = 0;
    }

    private readonly List<SupportRequest> preparedRequests = new List<SupportRequest>();
    private readonly List<User> checkedUsers = new List<User>();
    private readonly ReadCopies<User> readUsers = new ReadCopies<User>();
    private Action? concurrentChanges = null;

    // La version de la ligne de l'utilisateur lue par cette vue. Celle de la base (Store.UserRowVersion), comme xmin dans la vraie
    // base, change à chaque enregistrement qui écrit cette ligne, et quand une autre requête simulée enregistre une demande.
    private int? userRowVersionAtRead = null;

    // Ce que la fausse base contient.
    public List<User> SavedUsers => store.SavedUsers;
    public List<SupportRequest> SavedRequests => store.SavedRequests;
    public List<SupportResponse> SavedResponses => store.SavedResponses;

    // Ce que les tests vérifient : les comptes dont la ligne a été réécrite par un enregistrement réussi.
    public List<User> CommittedUserChecks => store.CommittedUserChecks;

    // Simule une autre requête qui enregistre cette demande juste après le compte des demandes : le nombre lu est déjà
    // ancien, et la ligne de l'utilisateur change de version.
    public SupportRequest? RequestSavedAfterCount
    {
        get => store.RequestSavedAfterCount;
        set => store.RequestSavedAfterCount = value;
    }

    // Simule d'autres requêtes qui écrivent les mêmes lignes juste avant chaque enregistrement : un conflit chacune.
    public int Conflicts
    {
        get => store.Conflicts;
        set => store.Conflicts = value;
    }

    // Simule une panne de la base : lire ou écrire les demandes échoue.
    public bool IsDown
    {
        get => store.IsDown;
        set => store.IsDown = value;
    }

    // Crée une fausse base sans compte, et sa première vue.
    public FakeSupportRequestRepository()
        : this(new Store())
    {
    }

    // Crée une fausse base qui partage cette liste de comptes, par exemple avec le faux dépôt des comptes, et sa première vue.
    public FakeSupportRequestRepository(List<User> savedUsers)
        : this(new Store { SavedUsers = savedUsers })
    {
    }

    // Crée une vue sur cette fausse base.
    private FakeSupportRequestRepository(Store store)
    {
        this.store = store;
        pendingChangesLock = store.Gate;
    }

    // Crée une autre vue sur la même fausse base, avec ses propres changements en attente.
    public FakeSupportRequestRepository CreateView()
    {
        return new FakeSupportRequestRepository(store);
    }

    // Lit la ligne de l'utilisateur, et note sa version.
    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            userRowVersionAtRead = store.UserRowVersion;
            var user = SavedUsers.SingleOrDefault(saved => saved.Id == userId);

            if (user != null)
            {
                readUsers.Keep(user);
            }

            return Task.FromResult(user);
        }
    }

    // Note le compte dont la ligne sera réécrite.
    public void PrepareUserCheck(User user)
    {
        lock (pendingChangesLock)
        {
            checkedUsers.Add(user);
        }
    }

    // Compte les demandes gardées de l'utilisateur depuis ce moment, ce moment compris.
    public Task<int> CountSentSinceAsync(Guid userId, DateTime since, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var count = SavedRequests.Count(request => request.UserId == userId && request.SentAt >= since);

            var otherRequest = RequestSavedAfterCount;
            if (otherRequest != null)
            {
                RequestSavedAfterCount = null;
                SavedRequests.Add(otherRequest);
                store.UserRowVersion = store.UserRowVersion + 1;
            }

            return Task.FromResult(count);
        }
    }

    // Garde la demande de côté : elle ne sera gardée qu'avec les autres changements.
    public void PrepareRequest(SupportRequest request)
    {
        lock (pendingChangesLock)
        {
            preparedRequests.Add(request);
        }
    }

    // Donne les demandes gardées de l'utilisateur, la plus récente d'abord, comme la vraie requête.
    public Task<List<SupportRequestSummary>> FindUserRequestsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var summaries = SavedRequests
                .Where(request => request.UserId == userId)
                .OrderByDescending(request => request.SentAt)
                .ThenBy(request => request.Id)
                .Select(request => new SupportRequestSummary(
                    request.Id,
                    request.SentAt,
                    request.Status,
                    SavedResponses.Count(response => response.SupportRequestId == request.Id)))
                .ToList();

            return Task.FromResult(summaries);
        }
    }

    // Donne la demande gardée de l'utilisateur, avec ses réponses de la plus ancienne à la plus récente, comme la vraie requête.
    public Task<SupportRequestDetails?> FindUserRequestAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var request = SavedRequests.SingleOrDefault(saved => saved.Id == requestId && saved.UserId == userId);

            if (request == null)
            {
                return Task.FromResult<SupportRequestDetails?>(null);
            }

            var responses = SavedResponses
                .Where(response => response.SupportRequestId == request.Id)
                .OrderBy(response => response.SentAt)
                .ThenBy(response => response.Id)
                .Select(response => new SupportResponseView(response.Content, response.SentAt))
                .ToList();

            return Task.FromResult<SupportRequestDetails?>(
                new SupportRequestDetails(request.Id, request.SentAt, request.Status, request.Message, responses));
        }
    }

    // Lit ce que dit l'e-mail d'une réponse : l'adresse du compte de la demande, gardée dans la fausse base des comptes, ou celle
    // du visiteur, comme la vraie requête.
    public Task<SupportResponseEmailContent?> ReadResponseEmailAsync(Guid responseId, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var response = SavedResponses.SingleOrDefault(saved => saved.Id == responseId);
            var request = SavedRequests.SingleOrDefault(saved => saved.Id == response?.SupportRequestId);

            if (response == null || request == null)
            {
                return Task.FromResult<SupportResponseEmailContent?>(null);
            }

            var account = SavedUsers.SingleOrDefault(user => user.Id == request.UserId);
            var address = account?.Email ?? request.ContactEmail!;

            return Task.FromResult<SupportResponseEmailContent?>(
                new SupportResponseEmailContent(address, request.SentAt, response.Content, request.UserId));
        }
    }

    // Efface tout de suite les demandes envoyées avant ce moment, avec leurs réponses, comme la vraie base.
    public Task<int> DeleteSentBeforeAsync(DateTime limit, CancellationToken cancellationToken = default)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var deletedIds = SavedRequests.Where(request => request.SentAt < limit).Select(request => request.Id).ToHashSet();
            SavedResponses.RemoveAll(response => deletedIds.Contains(response.SupportRequestId));

            return Task.FromResult(SavedRequests.RemoveAll(request => deletedIds.Contains(request.Id)));
        }
    }

    // La panne touche tout ce que l'enregistrement écrit pour le support.
    public void ThrowIfTableIsDown()
    {
        lock (pendingChangesLock)
        {
            if (IsDown && (preparedRequests.Count > 0 || checkedUsers.Count > 0))
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
            var isUserWritten = checkedUsers.Count > 0;
            var isWriting = preparedRequests.Count > 0 || isUserWritten;

            // Le contrôle de la ligne de l'utilisateur échoue si une autre requête l'a écrite depuis sa lecture.
            if (isUserWritten && userRowVersionAtRead != store.UserRowVersion)
            {
                return true;
            }

            if (Conflicts > 0 && isWriting)
            {
                Conflicts = Conflicts - 1;
                return true;
            }

            return false;
        }
    }

    // Remet les comptes lus comme ils étaient, et oublie ce qui a été préparé.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readUsers.RestoreAll();
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

    // Garde les demandes préparées. La ligne de l'utilisateur réécrite change de version, comme xmin dans la vraie base.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            SavedRequests.AddRange(preparedRequests);

            if (checkedUsers.Count > 0)
            {
                CommittedUserChecks.AddRange(checkedUsers);
                store.UserRowVersion = store.UserRowVersion + 1;
            }

            ClearTracking();
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

    // Oublie ce qui a été lu et préparé depuis le dernier enregistrement.
    private void ClearTracking()
    {
        preparedRequests.Clear();
        checkedUsers.Clear();
        readUsers.Clear();
        userRowVersionAtRead = null;
    }
}
