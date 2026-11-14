using WalletFlow.Application.Bags;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les sacs et leurs mouvements en mémoire. Ce qui est préparé n'est gardé
// qu'avec les autres changements (voir FakeDatabaseUnitOfWork). La suppression d'un sac suit les règles de la vraie base :
// ses mouvements avec l'argent disponible disparaissent, et ceux avec un autre sac gardent l'autre sac.
// Les requêtes SQL elles-mêmes sont testées sur la vraie base (BagRepositoryTests).
public class FakeBagRepository : IBagRepository, ITransactionalFakeRepository
{
    private readonly object pendingChangesLock = new object();
    private readonly List<Bag> preparedBags = new List<Bag>();
    private readonly List<BagMovement> preparedMovements = new List<BagMovement>();
    private readonly List<Bag> preparedDeletions = new List<Bag>();
    private readonly List<User> checkedUsers = new List<User>();
    private readonly ReadCopies<Bag> readBags = new ReadCopies<Bag>();
    private Action? concurrentChanges = null;

    // La version de la ligne de l'utilisateur, comme xmin dans la vraie base : elle change quand une autre requête simulée
    // enregistre un mouvement ou un sac. Le contrôle de la ligne échoue si elle a changé depuis sa lecture.
    private int userRowVersion = 0;
    private int? userRowVersionAtRead = null;

    // Ce que la fausse base contient. La liste des comptes peut être celle du faux dépôt des comptes, où l'inscription écrit.
    public List<User> SavedUsers { get; }
    public List<Bag> SavedBags { get; } = new List<Bag>();
    public List<BagMovement> SavedMovements { get; } = new List<BagMovement>();

    // Ce que les tests vérifient : les comptes dont la ligne a été réécrite par un enregistrement réussi.
    public List<User> CommittedUserChecks { get; } = new List<User>();

    // Simule une autre requête qui enregistre ce mouvement juste avant l'enregistrement d'un déplacement ou d'une création :
    // elle a réécrit la ligne de l'utilisateur, dont la version a changé.
    public BagMovement? MovementSavedBeforeSave { get; set; } = null;

    // Simule une autre requête qui crée ce sac juste avant l'enregistrement d'un déplacement ou d'une création.
    public Bag? BagCreatedBeforeSave { get; set; } = null;

    // Simule une autre requête qui supprime ce sac juste avant le prochain enregistrement qui écrit quelque chose.
    public Guid? BagDeletedBeforeSave { get; set; } = null;

    // Simule d'autres requêtes qui écrivent les mêmes lignes juste avant chaque enregistrement : un conflit chacune.
    public int Conflicts { get; set; } = 0;

    // Simule une autre requête qui enregistre ce mouvement juste après la lecture des soldes : les soldes lus sont déjà anciens,
    // et la ligne de l'utilisateur change de version.
    public BagMovement? MovementSavedAfterBalancesRead { get; set; } = null;

    // Simule une autre requête qui crée ce sac juste après le compte des sacs : le nombre lu est déjà ancien,
    // et la ligne de l'utilisateur change de version.
    public Bag? BagCreatedAfterCount { get; set; } = null;

    // Crée une fausse base sans compte.
    public FakeBagRepository()
    {
        SavedUsers = new List<User>();
    }

    // Crée une fausse base qui partage cette liste de comptes, par exemple avec le faux dépôt des comptes.
    public FakeBagRepository(List<User> savedUsers)
    {
        SavedUsers = savedUsers;
    }

    // Donne les sacs gardés pour cet utilisateur.
    public Task<List<Bag>> FindAllAsync(Guid userId)
    {
        lock (pendingChangesLock)
        {
            var bags = SavedBags.Where(bag => bag.UserId == userId).ToList();

            foreach (var bag in bags)
            {
                readBags.Keep(bag);
            }

            return Task.FromResult(bags);
        }
    }

    // Cherche le sac parmi ceux de l'utilisateur.
    public Task<Bag?> FindAsync(Guid userId, Guid bagId)
    {
        lock (pendingChangesLock)
        {
            var bag = SavedBags.SingleOrDefault(saved => saved.Id == bagId && saved.UserId == userId);

            if (bag != null)
            {
                readBags.Keep(bag);
            }

            return Task.FromResult(bag);
        }
    }

    // Compte les sacs gardés pour cet utilisateur, puis laisse passer l'autre requête simulée s'il y en a une.
    public Task<int> CountAsync(Guid userId)
    {
        lock (pendingChangesLock)
        {
            var count = SavedBags.Count(bag => bag.UserId == userId);

            var createdBag = BagCreatedAfterCount;
            if (createdBag != null)
            {
                BagCreatedAfterCount = null;
                SavedBags.Add(createdBag);
                userRowVersion = userRowVersion + 1;
            }

            return Task.FromResult(count);
        }
    }

    // Calcule le solde de chaque sac de l'utilisateur à partir des mouvements gardés.
    public Task<Dictionary<Guid, long>> SumBalancesAsync(Guid userId)
    {
        lock (pendingChangesLock)
        {
            var balances = new Dictionary<Guid, long>();

            foreach (var bag in SavedBags.Where(bag => bag.UserId == userId))
            {
                balances.Add(bag.Id, CountBalance(bag.Id));
            }

            var savedMovement = MovementSavedAfterBalancesRead;
            if (savedMovement != null)
            {
                MovementSavedAfterBalancesRead = null;
                SavedMovements.Add(savedMovement);
                userRowVersion = userRowVersion + 1;
            }

            return Task.FromResult(balances);
        }
    }

    // Compte les mouvements gardés depuis cette date dont un côté est un sac de l'utilisateur.
    public Task<int> CountMovementsSinceAsync(Guid userId, DateTime since)
    {
        lock (pendingChangesLock)
        {
            var userBagIds = SavedBags.Where(bag => bag.UserId == userId).Select(bag => bag.Id).ToList();
            var count = SavedMovements.Count(movement =>
                movement.MovedAt >= since
                && (IsIn(movement.SourceBagId, userBagIds) || IsIn(movement.DestinationBagId, userBagIds)));

            return Task.FromResult(count);
        }
    }

    // Donne la page demandée de l'historique, comme la vraie base : les plus récents d'abord, puis par identifiant.
    public Task<BagHistoryPage> FindHistoryAsync(Guid userId, Guid bagId, int page, int pageSize)
    {
        lock (pendingChangesLock)
        {
            if (!SavedBags.Any(bag => bag.Id == bagId && bag.UserId == userId))
            {
                return Task.FromResult(new BagHistoryPage(new List<BagHistoryItem>(), 0));
            }

            var movements = SavedMovements
                .Where(movement => movement.SourceBagId == bagId || movement.DestinationBagId == bagId)
                .OrderByDescending(movement => movement.MovedAt)
                .ThenByDescending(movement => movement.Id)
                .ToList();
            var items = new List<BagHistoryItem>();

            foreach (var movement in movements.Skip((page - 1) * pageSize).Take(pageSize))
            {
                var isIncoming = movement.DestinationBagId == bagId;
                var otherBagId = isIncoming ? movement.SourceBagId : movement.DestinationBagId;
                var otherBagName = SavedBags.SingleOrDefault(bag => bag.Id == otherBagId)?.Name;
                var amountInCents = isIncoming ? movement.AmountInCents : -movement.AmountInCents;
                items.Add(new BagHistoryItem(movement.Id, movement.MovedAt, amountInCents, otherBagId, otherBagName));
            }

            return Task.FromResult(new BagHistoryPage(items, movements.Count));
        }
    }

    // Cherche le compte gardé, et retient la version de sa ligne au moment de cette lecture.
    public Task<User?> FindUserAsync(Guid userId)
    {
        lock (pendingChangesLock)
        {
            userRowVersionAtRead = userRowVersion;
            return Task.FromResult(SavedUsers.SingleOrDefault(user => user.Id == userId));
        }
    }

    // Note le compte dont la ligne sera réécrite : les conflits de ce faux dépôt sont simulés par ses réglages.
    public void PrepareUserCheck(User user)
    {
        lock (pendingChangesLock)
        {
            checkedUsers.Add(user);
        }
    }

    // Garde le sac de côté : il ne sera gardé qu'avec les autres changements.
    public void PrepareBag(Bag bag)
    {
        lock (pendingChangesLock)
        {
            preparedBags.Add(bag);
        }
    }

    // Garde le mouvement de côté : il ne sera gardé qu'avec les autres changements.
    public void PrepareMovement(BagMovement movement)
    {
        lock (pendingChangesLock)
        {
            preparedMovements.Add(movement);
        }
    }

    // Garde la suppression de côté : elle ne sera faite qu'avec les autres changements.
    public void PrepareDeletion(Bag bag)
    {
        lock (pendingChangesLock)
        {
            preparedDeletions.Add(bag);
        }
    }

    // Aucune table de ce faux dépôt ne tombe en panne pendant un enregistrement.
    public void ThrowIfTableIsDown()
    {
    }

    // Décide si une autre requête écrit les mêmes lignes juste avant l'enregistrement, selon les réglages du test,
    // et prépare ce que cette autre requête aura écrit.
    public bool HasConcurrentChange()
    {
        lock (pendingChangesLock)
        {
            var changedBag = readBags.FindChangedSinceRead((copy, current) =>
                copy.Name != current.Name
                || copy.TargetAmountInCents != current.TargetAmountInCents
                || copy.TargetDate != current.TargetDate);
            var isWriting = preparedBags.Count > 0
                || preparedMovements.Count > 0
                || preparedDeletions.Count > 0
                || checkedUsers.Count > 0
                || changedBag != null;

            // Le contrôle de la ligne de l'utilisateur échoue si une autre requête l'a écrite depuis sa lecture.
            if (checkedUsers.Count > 0 && userRowVersionAtRead != userRowVersion)
            {
                return true;
            }

            var savedMovement = MovementSavedBeforeSave;
            if (savedMovement != null && checkedUsers.Count > 0)
            {
                MovementSavedBeforeSave = null;
                concurrentChanges = () => SavedMovements.Add(savedMovement);
                return true;
            }

            var createdBag = BagCreatedBeforeSave;
            if (createdBag != null && checkedUsers.Count > 0)
            {
                BagCreatedBeforeSave = null;
                concurrentChanges = () => SavedBags.Add(createdBag);
                return true;
            }

            if (BagDeletedBeforeSave is Guid deletedBagId && isWriting)
            {
                BagDeletedBeforeSave = null;
                concurrentChanges = () => DeleteLikeTheDatabase(deletedBagId);
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

    // Remet les sacs lus comme ils étaient, et oublie ce qui a été préparé.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readBags.RestoreAll();
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

    // Garde les sacs et les mouvements préparés, puis fait les suppressions préparées.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            SavedBags.AddRange(preparedBags);
            SavedMovements.AddRange(preparedMovements);
            CommittedUserChecks.AddRange(checkedUsers);

            foreach (var deletedBag in preparedDeletions)
            {
                DeleteLikeTheDatabase(deletedBag.Id);
            }

            ClearTracking();
        }
    }

    // Supprime le sac comme la vraie base : le déclencheur efface ses mouvements avec l'argent disponible,
    // puis ON DELETE SET NULL vide son côté dans les mouvements avec un autre sac.
    private void DeleteLikeTheDatabase(Guid bagId)
    {
        SavedBags.RemoveAll(bag => bag.Id == bagId);
        SavedMovements.RemoveAll(movement =>
            (movement.SourceBagId == bagId && movement.DestinationBagId == null)
            || (movement.DestinationBagId == bagId && movement.SourceBagId == null));

        foreach (var movement in SavedMovements)
        {
            if (movement.SourceBagId == bagId)
            {
                EntityValues.WriteProperty(movement, nameof(BagMovement.SourceBagId), null);
            }

            if (movement.DestinationBagId == bagId)
            {
                EntityValues.WriteProperty(movement, nameof(BagMovement.DestinationBagId), null);
            }
        }
    }

    // Calcule le solde d'un sac : les mouvements arrivés moins les mouvements partis.
    private long CountBalance(Guid bagId)
    {
        long balanceInCents = 0;

        foreach (var movement in SavedMovements)
        {
            if (movement.DestinationBagId == bagId)
            {
                balanceInCents = balanceInCents + movement.AmountInCents;
            }

            if (movement.SourceBagId == bagId)
            {
                balanceInCents = balanceInCents - movement.AmountInCents;
            }
        }

        return balanceInCents;
    }

    // Renvoie vrai si ce côté d'un mouvement est un de ces sacs.
    private static bool IsIn(Guid? bagId, List<Guid> bagIds)
    {
        return bagId is Guid id && bagIds.Contains(id);
    }

    // Oublie ce qui a été lu et préparé depuis le dernier enregistrement.
    private void ClearTracking()
    {
        preparedBags.Clear();
        preparedMovements.Clear();
        preparedDeletions.Clear();
        checkedUsers.Clear();
        readBags.Clear();
        userRowVersionAtRead = null;
    }
}
