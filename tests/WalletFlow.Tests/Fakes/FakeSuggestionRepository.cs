using WalletFlow.Application.Suggestions;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les suggestions en mémoire. Ce qui est préparé n'est gardé qu'avec les autres
// changements (voir FakeDatabaseUnitOfWork). Les économies réalisées se lisent dans les mouvements du faux dépôt des sacs.
// Les requêtes SQL elles-mêmes sont testées sur la vraie base (SuggestionRepositoryTests).
public class FakeSuggestionRepository : ISuggestionRepository, ITransactionalFakeRepository
{
    private readonly object pendingChangesLock = new object();
    private readonly FakeBagRepository bagRepository;
    private readonly List<SavingsSuggestion> preparedSuggestions = new List<SavingsSuggestion>();
    private readonly List<SavingsSuggestion> preparedDeletions = new List<SavingsSuggestion>();
    private readonly ReadCopies<SavingsSuggestion> readSuggestions = new ReadCopies<SavingsSuggestion>();
    private Action? concurrentChanges = null;

    // Ce que la fausse base contient.
    public List<SavingsSuggestion> SavedSuggestions { get; } = new List<SavingsSuggestion>();

    // Simule une autre requête qui enregistre ces changements juste avant le prochain enregistrement qui écrit une suggestion :
    // par exemple une réponse à la même suggestion, ou un calcul du même mois. L'enregistrement en cours échoue alors.
    public Action? OtherRequestBeforeSave { get; set; } = null;

    // Simule d'autres requêtes qui écrivent les mêmes lignes juste avant chaque enregistrement : un conflit chacune.
    public int Conflicts { get; set; } = 0;

    // Le faux dépôt des opérations, où se voit qu'un utilisateur en a au moins une. Sans lui, aucun utilisateur n'en a.
    public FakeTransactionRepository? Transactions { get; set; } = null;

    // Simule une panne de la base pour les suggestions de cet utilisateur : les lire échoue.
    public Guid? FailingUserId { get; set; } = null;

    // Simule une panne de la base pendant la recherche des utilisateurs à calculer.
    public bool FailsWhenListingUsers { get; set; } = false;

    // Relie la fausse base des suggestions à celle des sacs, où sont les mouvements.
    public FakeSuggestionRepository(FakeBagRepository bagRepository)
    {
        this.bagRepository = bagRepository;
    }

    // Donne les suggestions gardées pour cet utilisateur.
    public Task<List<SavingsSuggestion>> FindAllAsync(Guid userId)
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
    public Task<SavingsSuggestion?> FindAsync(Guid userId, Guid suggestionId)
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
    public async Task<List<Guid>> FindUsersToCalculateAsync(DateOnly month, Guid? afterUserId, int count)
    {
        if (FailsWhenListingUsers)
        {
            throw new InvalidOperationException("Panne simulée de la recherche des utilisateurs.");
        }

        var dueUsers = new List<Guid>();

        foreach (var user in bagRepository.SavedUsers.ToList())
        {
            var hasTransaction = Transactions != null && await Transactions.FindEarliestDateAsync(user.Id, null) != null;
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
    public Task<long> SumSavedAsync(Guid userId)
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
