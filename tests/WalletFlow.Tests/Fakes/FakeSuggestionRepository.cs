using WalletFlow.Application.Suggestions;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les suggestions en mémoire. Ce qui est préparé n'est gardé qu'avec les autres
// changements (voir FakeDatabaseUnitOfWork). Les économies réalisées se lisent dans les mouvements du faux dépôt des sacs.
// Les requêtes SQL elles-mêmes sont testées sur la vraie base (SuggestionRepositoryTests). Chaque vue (voir CreateView) a ses propres changements en attente, comme un DbContext, sur la même fausse base.
public class FakeSuggestionRepository : ISuggestionRepository, ITransactionalFakeRepository
{
    private readonly Store store;
    private readonly object pendingChangesLock;

    // La fausse base, commune à toutes les vues : ce qui est enregistré, les liens, et les pannes et requêtes simulées.
    private sealed class Store
    {
        public object Gate { get; } = new object();

        public List<SavingsSuggestion> SavedSuggestions { get; set; } = new List<SavingsSuggestion>();

        public Action? OtherRequestBeforeSave { get; set; } = null;

        public int Conflicts { get; set; } = 0;

        public FakeTransactionRepository? Transactions { get; set; } = null;

        public Guid? FailingUserId { get; set; } = null;

        public bool FailsWhenListingUsers { get; set; } = false;
    }

    private readonly FakeBagRepository bagRepository;
    private readonly List<SavingsSuggestion> preparedSuggestions = new List<SavingsSuggestion>();
    private readonly List<SavingsSuggestion> preparedDeletions = new List<SavingsSuggestion>();
    private readonly ReadCopies<SavingsSuggestion> readSuggestions = new ReadCopies<SavingsSuggestion>();
    private Action? concurrentChanges = null;

    // Ce que la fausse base contient.
    public List<SavingsSuggestion> SavedSuggestions => store.SavedSuggestions;

    // Simule une autre requête qui enregistre ces changements juste avant le prochain enregistrement qui écrit une suggestion :
    // par exemple une réponse à la même suggestion, ou un calcul du même mois. L'enregistrement en cours échoue alors.
    public Action? OtherRequestBeforeSave
    {
        get => store.OtherRequestBeforeSave;
        set => store.OtherRequestBeforeSave = value;
    }

    // Simule d'autres requêtes qui écrivent les mêmes lignes juste avant chaque enregistrement : un conflit chacune.
    public int Conflicts
    {
        get => store.Conflicts;
        set => store.Conflicts = value;
    }

    // Le faux dépôt des opérations, où se voit qu'un utilisateur en a au moins une. Sans lui, aucun utilisateur n'en a.
    public FakeTransactionRepository? Transactions
    {
        get => store.Transactions;
        set => store.Transactions = value;
    }

    // Simule une panne de la base pour les suggestions de cet utilisateur : les lire échoue.
    public Guid? FailingUserId
    {
        get => store.FailingUserId;
        set => store.FailingUserId = value;
    }

    // Simule une panne de la base pendant la recherche des utilisateurs à calculer.
    public bool FailsWhenListingUsers
    {
        get => store.FailsWhenListingUsers;
        set => store.FailsWhenListingUsers = value;
    }

    // Relie la fausse base des suggestions à celle des sacs, où sont les mouvements, et crée sa première vue.
    public FakeSuggestionRepository(FakeBagRepository bagRepository)
        : this(new Store(), bagRepository)
    {
    }

    // Crée une vue sur cette fausse base.
    private FakeSuggestionRepository(Store store, FakeBagRepository bagRepository)
    {
        this.store = store;
        pendingChangesLock = store.Gate;
        this.bagRepository = bagRepository;
    }

    // Crée une autre vue sur la même fausse base, avec ses propres changements en attente.
    public FakeSuggestionRepository CreateView()
    {
        return new FakeSuggestionRepository(store, bagRepository);
    }

    // Donne les suggestions gardées pour cet utilisateur.
    public Task<List<SavingsSuggestion>> FindAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == FailingUserId)
        {
            throw new InvalidOperationException("Panne simulée des suggestions de cet utilisateur.");
        }

        lock (pendingChangesLock)
        {
            var suggestions = SavedSuggestions.Where(suggestion => suggestion.UserId == userId).ToList();

            foreach (var suggestion in suggestions)
            {
                readSuggestions.Keep(suggestion);
            }

            return Task.FromResult(suggestions);
        }
    }

    // Cherche la suggestion parmi celles de l'utilisateur.
    public Task<SavingsSuggestion?> FindAsync(Guid userId, Guid suggestionId, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            var suggestion = SavedSuggestions.SingleOrDefault(saved => saved.Id == suggestionId && saved.UserId == userId);

            if (suggestion != null)
            {
                readSuggestions.Keep(suggestion);
            }

            return Task.FromResult(suggestion);
        }
    }

    // Donne les utilisateurs gardés dont ce mois attend son calcul, dans l'ordre de leur identifiant comme la vraie base :
    // PostgreSQL range les uuid octet par octet, comme cette comparaison de leur texte.
    public async Task<List<Guid>> FindUsersToCalculateAsync(DateOnly month, Guid? afterUserId, int count, CancellationToken cancellationToken = default)
    {
        if (FailsWhenListingUsers)
        {
            throw new InvalidOperationException("Panne simulée de la recherche des utilisateurs.");
        }

        var dueUsers = new List<Guid>();

        foreach (var user in bagRepository.SavedUsers.ToList())
        {
            var hasTransaction = Transactions != null && await Transactions.FindEarliestDateAsync(user.Id, null, cancellationToken) != null;
            var isDue = user.IsEmailConfirmed
                && (user.LastSuggestionMonth == null || user.LastSuggestionMonth < month)
                && hasTransaction
                && (afterUserId == null || string.CompareOrdinal(user.Id.ToString(), afterUserId.Value.ToString()) > 0);

            if (isDue)
            {
                dueUsers.Add(user.Id);
            }
        }

        return dueUsers
            .OrderBy(userId => userId.ToString(), StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }

    // Additionne les mouvements gardés qui viennent des suggestions acceptées de l'utilisateur.
    public Task<long> SumSavedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            var acceptedIds = SavedSuggestions
                .Where(suggestion => suggestion.UserId == userId && suggestion.Status == SuggestionStatus.Accepted)
                .Select(suggestion => suggestion.Id)
                .ToList();
            var savedInCents = bagRepository.SavedMovements
                .Where(movement => movement.SavingsSuggestionId is Guid id && acceptedIds.Contains(id))
                .Sum(movement => (long)movement.AmountInCents);

            return Task.FromResult(savedInCents);
        }
    }

    // Garde la suggestion de côté : elle ne sera gardée qu'avec les autres changements.
    public void PrepareSuggestion(SavingsSuggestion suggestion)
    {
        lock (pendingChangesLock)
        {
            preparedSuggestions.Add(suggestion);
        }
    }

    // Garde l'effacement de côté : il ne sera fait qu'avec les autres changements.
    public void PrepareDeletion(SavingsSuggestion suggestion)
    {
        lock (pendingChangesLock)
        {
            preparedDeletions.Add(suggestion);
        }
    }

    // Aucune table de ce faux dépôt ne tombe en panne pendant un enregistrement.
    public void ThrowIfTableIsDown()
    {
    }

    // Décide si une autre requête écrit les mêmes lignes juste avant l'enregistrement, selon les réglages du test.
    public bool HasConcurrentChange()
    {
        lock (pendingChangesLock)
        {
            var answeredSuggestion = readSuggestions.FindChangedSinceRead((copy, current) =>
                copy.Status != current.Status || copy.StatusChangedAt != current.StatusChangedAt);
            var isWriting = preparedSuggestions.Count > 0 || preparedDeletions.Count > 0 || answeredSuggestion != null;
            var otherRequest = OtherRequestBeforeSave;

            if (otherRequest != null && isWriting)
            {
                OtherRequestBeforeSave = null;
                concurrentChanges = otherRequest;
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

    // Remet les suggestions lues comme elles étaient, et oublie ce qui a été préparé.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readSuggestions.RestoreAll();
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

        changes?.Invoke();
    }

    // Garde les suggestions préparées, puis fait les effacements préparés.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            SavedSuggestions.AddRange(preparedSuggestions);

            foreach (var deleted in preparedDeletions)
            {
                SavedSuggestions.Remove(deleted);
            }

            ClearTracking();
        }
    }

    // Oublie ce qui a été lu et préparé depuis le dernier enregistrement.
    private void ClearTracking()
    {
        preparedSuggestions.Clear();
        preparedDeletions.Clear();
        readSuggestions.Clear();
    }
}
