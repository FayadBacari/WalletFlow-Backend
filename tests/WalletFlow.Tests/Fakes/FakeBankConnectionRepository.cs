using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les connexions bancaires et leurs comptes en mémoire.
// Ce qui est préparé n'est gardé qu'avec les autres changements (voir FakeDatabaseUnitOfWork).
// Les requêtes de la vraie base sont testées sur la vraie base (BankConnectionRepositoryTests). Chaque vue (voir CreateView) a ses propres changements en attente, comme un DbContext, sur la même fausse base.
public class FakeBankConnectionRepository : IBankConnectionRepository, ITransactionalFakeRepository
{
    private readonly Store store;
    private readonly object pendingChangesLock;

    // La fausse base, commune à toutes les vues : ce qui est enregistré, les comptes, ce que les tests vérifient, et les pannes et requêtes simulées.
    private sealed class Store
    {
        public object Gate { get; } = new object();

        public List<BankConnection> SavedConnections { get; set; } = new List<BankConnection>();

        public List<BankAccount> SavedAccounts { get; set; } = new List<BankAccount>();

        public int Conflicts { get; set; } = 0;

        public bool DeleteReadConnectionBeforeSave { get; set; } = false;

        public string? RenewalBeforeDeletion { get; set; } = null;

        public int StoppedDeletions { get; set; } = 0;

        public Action<BankConnection>? OtherRequestBeforeCheckedSave { get; set; } = null;

        public List<BankConnection> CommittedChecks { get; set; } = new List<BankConnection>();

        public Guid? FailingConnectionId { get; set; } = null;

        public bool FailsWhenListingRevocations { get; set; } = false;

        public List<User> Users { get; set; } = new List<User>();
    }

    private readonly List<BankConnection> preparedConnections = new List<BankConnection>();
    private readonly List<BankAccount> preparedAccounts = new List<BankAccount>();
    private readonly ReadCopies<BankConnection> readConnections = new ReadCopies<BankConnection>();
    private readonly ReadCopies<BankAccount> readAccounts = new ReadCopies<BankAccount>();
    private readonly List<BankConnection> checkedConnections = new List<BankConnection>();
    private Action? concurrentChanges = null;

    // Ce que la fausse base contient.
    public List<BankConnection> SavedConnections => store.SavedConnections;
    public List<BankAccount> SavedAccounts => store.SavedAccounts;

    // Simule d'autres requêtes qui écrivent les mêmes lignes juste avant chaque enregistrement : un conflit chacune.
    public int Conflicts
    {
        get => store.Conflicts;
        set => store.Conflicts = value;
    }

    // Simule une autre requête qui supprime la connexion lue, juste avant l'enregistrement de son changement.
    public bool DeleteReadConnectionBeforeSave
    {
        get => store.DeleteReadConnectionBeforeSave;
        set => store.DeleteReadConnectionBeforeSave = value;
    }

    // Simule un renouvellement qui change la session de la connexion juste avant sa suppression : la valeur est la nouvelle session.
    public string? RenewalBeforeDeletion
    {
        get => store.RenewalBeforeDeletion;
        set => store.RenewalBeforeDeletion = value;
    }

    // Simule des suppressions arrêtées par PostgreSQL pour débloquer un interblocage : une chacune, rien n'est supprimé.
    public int StoppedDeletions
    {
        get => store.StoppedDeletions;
        set => store.StoppedDeletions = value;
    }

    // Simule une autre requête qui écrit une connexion contrôlée juste avant l'enregistrement, par exemple un renouvellement :
    // comme la version de la vraie ligne, l'enregistrement échoue, et le passage suivant relit ce que l'autre a écrit.
    public Action<BankConnection>? OtherRequestBeforeCheckedSave
    {
        get => store.OtherRequestBeforeCheckedSave;
        set => store.OtherRequestBeforeCheckedSave = value;
    }

    // Les connexions réécrites avec leur version par un enregistrement réussi.
    public List<BankConnection> CommittedChecks => store.CommittedChecks;

    // Simule une panne de la base pour cette connexion : la lire par son identifiant échoue.
    public Guid? FailingConnectionId
    {
        get => store.FailingConnectionId;
        set => store.FailingConnectionId = value;
    }

    // Simule une panne de la base au moment de lister les connexions à révoquer.
    public bool FailsWhenListingRevocations
    {
        get => store.FailsWhenListingRevocations;
        set => store.FailsWhenListingRevocations = value;
    }

    // Crée une fausse base vide, et sa première vue.
    public FakeBankConnectionRepository()
        : this(new Store())
    {
    }

    // Crée une vue sur cette fausse base.
    private FakeBankConnectionRepository(Store store)
    {
        this.store = store;
        pendingChangesLock = store.Gate;
    }

    // Crée une autre vue sur la même fausse base, avec ses propres changements en attente.
    public FakeBankConnectionRepository CreateView()
    {
        return new FakeBankConnectionRepository(store);
    }

    // Donne les connexions gardées pour cet utilisateur.
    public Task<List<BankConnection>> FindConnectionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            var connections = SavedConnections.Where(connection => connection.UserId == userId).ToList();
            connections.ForEach(readConnections.Keep);
            return Task.FromResult(connections);
        }
    }

    // Cherche la connexion parmi celles de l'utilisateur.
    public Task<BankConnection?> FindConnectionAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            var connection = SavedConnections.SingleOrDefault(saved => saved.Id == connectionId && saved.UserId == userId);
            KeepConnection(connection);
            return Task.FromResult(connection);
        }
    }

    // Cherche la connexion par son seul identifiant.
    public Task<BankConnection?> FindConnectionByIdAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        if (connectionId == FailingConnectionId)
        {
            throw new InvalidOperationException("Panne simulée de cette connexion.");
        }

        lock (pendingChangesLock)
        {
            var connection = SavedConnections.SingleOrDefault(saved => saved.Id == connectionId);
            KeepConnection(connection);
            return Task.FromResult(connection);
        }
    }

    // Relit l'identifiant chez le prestataire, tel qu'il est gardé maintenant.
    public Task<string?> ReadExternalIdAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            var connection = SavedConnections.SingleOrDefault(saved => saved.Id == connectionId && saved.UserId == userId);
            return Task.FromResult(connection?.ExternalConnectionId);
        }
    }

    // Donne les comptes gardés pour ces connexions.
    public Task<List<BankAccount>> FindAccountsAsync(List<Guid> connectionIds, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            var accounts = SavedAccounts.Where(account => connectionIds.Contains(account.BankConnectionId)).ToList();
            accounts.ForEach(readAccounts.Keep);
            return Task.FromResult(accounts);
        }
    }

    // Les comptes de la fausse base, pour savoir lesquels sont en suppression. Sans eux, aucun compte ne l'est.
    public List<User> Users
    {
        get => store.Users;
        set => store.Users = value;
    }

    // Donne les connexions qui ne sont pas expirées, sauf celles des comptes en suppression.
    public Task<List<Guid>> FindConnectionIdsToUpdateAsync(CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            var connectionIds = SavedConnections
                .Where(connection => connection.Status != BankConnectionStatus.Expired && !IsBeingDeleted(connection.UserId))
                .Select(connection => connection.Id)
                .ToList();
            return Task.FromResult(connectionIds);
        }
    }

    // Cherche ce compte dans les autres connexions de l'utilisateur.
    public Task<bool> IsAccountConnectedElsewhereAsync(Guid userId, string stableId, Guid? exceptConnectionId, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            var isConnected = SavedAccounts.Any(account =>
                account.ExternalAccountId == stableId
                && account.BankConnectionId != exceptConnectionId
                && SavedConnections.Any(connection => connection.Id == account.BankConnectionId && connection.UserId == userId));
            return Task.FromResult(isConnected);
        }
    }

    // Garde la connexion de côté : elle ne sera gardée qu'avec les autres changements.
    public void PrepareConnection(BankConnection connection)
    {
        lock (pendingChangesLock)
        {
            preparedConnections.Add(connection);
        }
    }

    // Garde le compte de côté : il ne sera gardé qu'avec les autres changements.
    public void PrepareAccount(BankAccount account)
    {
        lock (pendingChangesLock)
        {
            preparedAccounts.Add(account);
        }
    }

    // Note la connexion dont la ligne sera réécrite avec sa version.
    public void PrepareUnchangedCheck(BankConnection connection)
    {
        lock (pendingChangesLock)
        {
            checkedConnections.Add(connection);
        }
    }

    // Supprime la connexion et ses comptes si sa session est toujours celle-ci, comme la vraie base.
    public Task<bool> DeleteAsync(Guid userId, Guid connectionId, string externalConnectionId, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            var connection = SavedConnections.SingleOrDefault(saved => saved.Id == connectionId && saved.UserId == userId);

            if (connection != null && RenewalBeforeDeletion != null)
            {
                EntityValues.WriteProperty(connection, nameof(BankConnection.ExternalConnectionId), RenewalBeforeDeletion);
                RenewalBeforeDeletion = null;
            }

            if (connection == null || connection.ExternalConnectionId != externalConnectionId)
            {
                return Task.FromResult(false);
            }

            if (StoppedDeletions > 0)
            {
                StoppedDeletions = StoppedDeletions - 1;
                return Task.FromResult(false);
            }

            SavedConnections.Remove(connection);
            SavedAccounts.RemoveAll(account => account.BankConnectionId == connectionId);
            return Task.FromResult(true);
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
            var changedConnection = readConnections.FindChangedSinceRead((copy, current) =>
                copy.Status != current.Status
                || copy.ExternalConnectionId != current.ExternalConnectionId
                || copy.AuthorizationExpiresAt != current.AuthorizationExpiresAt);
            var changedAccount = readAccounts.FindChangedSinceRead((copy, current) =>
                copy.BalanceInCents != current.BalanceInCents
                || copy.BalanceUpdatedAt != current.BalanceUpdatedAt
                || copy.Name != current.Name);
            var isWriting = preparedConnections.Count > 0 || preparedAccounts.Count > 0 || changedConnection != null || changedAccount != null
                || checkedConnections.Count > 0;

            var otherRequest = OtherRequestBeforeCheckedSave;
            if (otherRequest != null && checkedConnections.Count > 0)
            {
                OtherRequestBeforeCheckedSave = null;
                var checkedConnection = checkedConnections[0];
                concurrentChanges = () => otherRequest(checkedConnection);
                return true;
            }

            if (DeleteReadConnectionBeforeSave && changedConnection != null)
            {
                DeleteReadConnectionBeforeSave = false;
                var deletedId = changedConnection.Id;
                concurrentChanges = () =>
                {
                    SavedConnections.RemoveAll(connection => connection.Id == deletedId);
                    SavedAccounts.RemoveAll(account => account.BankConnectionId == deletedId);
                };
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

    // Remet les objets lus comme ils étaient, et oublie ce qui a été préparé.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readConnections.RestoreAll();
            readAccounts.RestoreAll();
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

    // Garde ce qui a été préparé.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            SavedConnections.AddRange(preparedConnections);
            SavedAccounts.AddRange(preparedAccounts);
            CommittedChecks.AddRange(checkedConnections);
            ClearTracking();
        }
    }

    // Garde une copie de la connexion lue, si elle existe.
    private void KeepConnection(BankConnection? connection)
    {
        if (connection != null)
        {
            readConnections.Keep(connection);
        }
    }

    // Oublie ce qui a été lu et préparé depuis le dernier enregistrement.
    private void ClearTracking()
    {
        preparedConnections.Clear();
        preparedAccounts.Clear();
        checkedConnections.Clear();
        readConnections.Clear();
        readAccounts.Clear();
    }

    // Donne les connexions pas encore expirées des comptes en suppression.
    public Task<List<Guid>> FindConnectionIdsToRevokeAsync(CancellationToken cancellationToken = default)
    {
        if (FailsWhenListingRevocations)
        {
            throw new InvalidOperationException("Panne simulée de la liste des connexions à révoquer.");
        }

        lock (pendingChangesLock)
        {
            var connectionIds = SavedConnections
                .Where(connection => connection.Status != BankConnectionStatus.Expired && IsBeingDeleted(connection.UserId))
                .Select(connection => connection.Id)
                .ToList();
            return Task.FromResult(connectionIds);
        }
    }

    // Renvoie vrai si la suppression de ce compte est demandée.
    private bool IsBeingDeleted(Guid userId)
    {
        return Users.Any(user => user.Id == userId && user.DeletionRequestedAt != null);
    }
}
