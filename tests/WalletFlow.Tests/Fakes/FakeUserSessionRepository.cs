using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les sessions et les appareils en mémoire.
// Les sessions et les appareils préparés ne sont gardés qu'avec les autres changements (voir FakeDatabaseUnitOfWork).
// Chaque vue (voir CreateView) a ses propres changements en attente, comme un DbContext, sur la même fausse base.
public class FakeUserSessionRepository : IUserSessionRepository, ITransactionalFakeRepository
{
    public const string FailureDetail = "detail-technique-de-la-panne-des-sessions";

    private readonly Store store;
    private readonly object pendingChangesLock;

    // La fausse base, commune à toutes les vues : ce qui est enregistré, ce que les tests vérifient, et les pannes et conflits simulés.
    private sealed class Store
    {
        public object Gate { get; } = new object();

        public List<UserSession> SavedSessions { get; set; } = new List<UserSession>();

        public List<Device> SavedDevices { get; set; } = new List<Device>();

        public List<DateTime> CleanupDates { get; set; } = new List<DateTime>();

        public List<DateTime> CreationCleanupDates { get; set; } = new List<DateTime>();

        public List<DateTime> DeviceCleanupDates { get; set; } = new List<DateTime>();

        public int SessionSearchCount { get; set; } = 0;

        public bool IsDown { get; set; } = false;

        public bool FailsWhenClosingAllSessions { get; set; } = false;

        public bool FailsDuringCleanup { get; set; } = false;

        public int LoginConflicts { get; set; } = 0;
    }

    private readonly List<UserSession> preparedSessions = new List<UserSession>();
    private readonly List<Device> preparedDevices = new List<Device>();
    private readonly List<Guid> accountsLosingAllSessions = new List<Guid>();
    private readonly List<UserSession> preparedSessionRemovals = new List<UserSession>();
    private readonly List<Device> preparedDeviceRemovals = new List<Device>();
    private readonly List<(Device Device, DateTime UsedAt)> preparedDeviceUses = new List<(Device Device, DateTime UsedAt)>();

    // Ce que la fausse base contient.
    public List<UserSession> SavedSessions => store.SavedSessions;
    public List<Device> SavedDevices => store.SavedDevices;

    // Ce que les tests vérifient : les passages du nettoyage et les recherches de session.
    public List<DateTime> CleanupDates => store.CleanupDates;
    public List<DateTime> CreationCleanupDates => store.CreationCleanupDates;
    public List<DateTime> DeviceCleanupDates => store.DeviceCleanupDates;
    public int SessionSearchCount
    {
        get => store.SessionSearchCount;
        private set => store.SessionSearchCount = value;
    }

    // Simule une panne de la base : enregistrer une connexion ou chercher une session échoue.
    public bool IsDown
    {
        get => store.IsDown;
        set => store.IsDown = value;
    }

    // Simule une panne de la base au moment de fermer toutes les sessions d'un compte.
    public bool FailsWhenClosingAllSessions
    {
        get => store.FailsWhenClosingAllSessions;
        set => store.FailsWhenClosingAllSessions = value;
    }

    // Simule une panne de la base pendant l'effacement des sessions expirées.
    public bool FailsDuringCleanup
    {
        get => store.FailsDuringCleanup;
        set => store.FailsDuringCleanup = value;
    }

    // Simule d'autres requêtes qui changent le compte juste avant l'enregistrement de la connexion : un conflit chacune.
    public int LoginConflicts
    {
        get => store.LoginConflicts;
        set => store.LoginConflicts = value;
    }

    // Crée une fausse base vide, et sa première vue.
    public FakeUserSessionRepository()
        : this(new Store())
    {
    }

    // Crée une vue sur cette fausse base.
    private FakeUserSessionRepository(Store store)
    {
        this.store = store;
        pendingChangesLock = store.Gate;
    }

    // Crée une autre vue sur la même fausse base, avec ses propres changements en attente.
    public FakeUserSessionRepository CreateView()
    {
        return new FakeUserSessionRepository(store);
    }

    // Garde la session de côté : elle ne sera gardée qu'avec les autres changements.
    public void PrepareSession(UserSession session)
    {
        lock (pendingChangesLock)
        {
            preparedSessions.Add(session);
        }
    }

    // Garde de côté la fermeture de toutes les sessions du compte : elle ne se fera qu'avec les autres changements.
    public Task PrepareAllSessionsRemovalAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            accountsLosingAllSessions.Add(userId);
        }

        return Task.CompletedTask;
    }

    // Garde l'appareil de côté : il ne sera gardé qu'avec les autres changements.
    public void PrepareDevice(Device device)
    {
        lock (pendingChangesLock)
        {
            preparedDevices.Add(device);
        }
    }

    // Cherche en mémoire cette empreinte de jeton d'appareil, pour ce compte.
    public Task<bool> DeviceBelongsToAccountAsync(string deviceTokenHash, Guid accountId, CancellationToken cancellationToken = default)
    {
        foreach (var device in SavedDevices)
        {
            if (device.DeviceToken == deviceTokenHash && device.UserId == accountId)
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    // Cherche en mémoire un appareil de ce compte.
    public Task<bool> HasDeviceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            return Task.FromResult(SavedDevices.Any(device => device.UserId == accountId));
        }
    }

    // Cherche en mémoire une session par son empreinte.
    public Task<UserSession?> FindAsync(string sessionTokenHash, CancellationToken cancellationToken = default)
    {
        if (IsDown)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        SessionSearchCount = SessionSearchCount + 1;

        foreach (var session in SavedSessions)
        {
            if (session.Id == sessionTokenHash)
            {
                return Task.FromResult<UserSession?>(session);
            }
        }

        return Task.FromResult<UserSession?>(null);
    }

    // Avance la date de dernière activité de la session gardée en mémoire.
    public Task RecordActivityAsync(string sessionTokenHash, DateTime now, CancellationToken cancellationToken = default)
    {
        foreach (var session in SavedSessions)
        {
            if (session.Id == sessionTokenHash)
            {
                session.RecordActivity(now);
            }
        }

        return Task.CompletedTask;
    }

    // Retire la session de la liste.
    public Task DeleteAsync(string sessionTokenHash, CancellationToken cancellationToken = default)
    {
        SavedSessions.RemoveAll(session => session.Id == sessionTokenHash);
        return Task.CompletedTask;
    }

    // Retire de la liste toutes les sessions du compte.
    public Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (FailsWhenClosingAllSessions)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        SavedSessions.RemoveAll(session => session.UserId == userId);
        return Task.CompletedTask;
    }

    // Garde les dates du passage. La vraie règle d'effacement est testée sur la vraie base. Comme EF Core, un jeton annulé
    // arrête tout.
    public Task<int> DeleteExpiredAsync(DateTime activityCutoff, DateTime creationCutoff, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FailsDuringCleanup)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        CleanupDates.Add(activityCutoff);
        CreationCleanupDates.Add(creationCutoff);
        return Task.FromResult(0);
    }

    // Garde de côté la fermeture des sessions les moins récemment actives du compte, au-delà de ce nombre.
    public Task PrepareOldestSessionsRemovalAsync(Guid userId, int keptCount, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            preparedSessionRemovals.AddRange(SavedSessions
                .Where(session => session.UserId == userId)
                .OrderByDescending(session => session.LastActivityAt)
                .ThenBy(session => session.Id, StringComparer.Ordinal)
                .Skip(keptCount));
        }

        return Task.CompletedTask;
    }

    // Garde de côté l'oubli des appareils les moins récemment utilisés du compte, au-delà de ce nombre.
    public Task PrepareOldestDevicesRemovalAsync(Guid userId, int keptCount, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            preparedDeviceRemovals.AddRange(SavedDevices
                .Where(device => device.UserId == userId)
                .OrderByDescending(device => device.LastUsedAt)
                .ThenBy(device => device.Id)
                .Skip(keptCount));
        }

        return Task.CompletedTask;
    }

    // Garde de côté la connexion depuis cet appareil du compte : elle ne sera notée qu'avec les autres changements.
    public Task PrepareDeviceUseAsync(string deviceTokenHash, Guid accountId, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (pendingChangesLock)
        {
            var device = SavedDevices.SingleOrDefault(saved => saved.DeviceToken == deviceTokenHash && saved.UserId == accountId);

            if (device != null)
            {
                preparedDeviceUses.Add((device, now));
            }
        }

        return Task.CompletedTask;
    }

    // Garde la date du passage. La vraie règle d'effacement est testée sur la vraie base.
    public Task<int> DeleteForgottenDevicesAsync(DateTime usedBefore, CancellationToken cancellationToken = default)
    {
        DeviceCleanupDates.Add(usedBefore);
        return Task.FromResult(0);
    }

    // La panne simulée touche l'enregistrement d'une connexion.
    public void ThrowIfTableIsDown()
    {
        lock (pendingChangesLock)
        {
            if (IsDown && preparedSessions.Count > 0)
            {
                throw new InvalidOperationException(FailureDetail);
            }
        }
    }

    // Une autre requête change le compte juste avant l'enregistrement de la connexion, tant que le réglage le demande.
    public bool HasConcurrentChange()
    {
        lock (pendingChangesLock)
        {
            if (preparedSessions.Count > 0 && LoginConflicts > 0)
            {
                LoginConflicts = LoginConflicts - 1;
                return true;
            }

            return false;
        }
    }

    // Oublie les sessions, les appareils et les fermetures préparés.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            preparedSessions.Clear();
            preparedDevices.Clear();
            accountsLosingAllSessions.Clear();
            preparedSessionRemovals.Clear();
            preparedDeviceRemovals.Clear();
            preparedDeviceUses.Clear();
        }
    }

    // L'autre requête simulée a changé le compte, pas les sessions : il n'y a rien à écrire ici.
    public void ApplyConcurrentChanges()
    {
    }

    // Ferme d'abord les sessions des comptes concernés, puis garde les sessions et les appareils préparés :
    // une nouvelle session préparée avec la fermeture lui survit, comme dans la vraie base.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            foreach (var accountId in accountsLosingAllSessions)
            {
                SavedSessions.RemoveAll(session => session.UserId == accountId);
            }

            SavedSessions.RemoveAll(preparedSessionRemovals.Contains);
            SavedDevices.RemoveAll(preparedDeviceRemovals.Contains);

            foreach (var (device, usedAt) in preparedDeviceUses)
            {
                device.RecordUse(usedAt);
            }

            SavedSessions.AddRange(preparedSessions);
            SavedDevices.AddRange(preparedDevices);
            preparedSessions.Clear();
            preparedDevices.Clear();
            accountsLosingAllSessions.Clear();
            preparedSessionRemovals.Clear();
            preparedDeviceRemovals.Clear();
            preparedDeviceUses.Clear();
        }
    }
}
