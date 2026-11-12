using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les sessions et les appareils en mémoire.
// Les sessions et les appareils préparés ne sont gardés qu'avec les autres changements (voir FakeDatabaseUnitOfWork).
public class FakeUserSessionRepository : IUserSessionRepository, ITransactionalFakeRepository
{
    public const string FailureDetail = "detail-technique-de-la-panne-des-sessions";

    private readonly object pendingChangesLock = new object();
    private readonly List<UserSession> preparedSessions = new List<UserSession>();
    private readonly List<Device> preparedDevices = new List<Device>();
    private readonly List<Guid> accountsLosingAllSessions = new List<Guid>();

    // Ce que la fausse base contient.
    public List<UserSession> SavedSessions { get; } = new List<UserSession>();
    public List<Device> SavedDevices { get; } = new List<Device>();

    // Ce que les tests vérifient : les passages du nettoyage et les recherches de session.
    public List<DateTime> CleanupDates { get; } = new List<DateTime>();
    public int SessionSearchCount { get; private set; } = 0;

    // Simule une panne de la base : enregistrer une connexion ou chercher une session échoue.
    public bool IsDown { get; set; } = false;

    // Simule une panne de la base au moment de fermer toutes les sessions d'un compte.
    public bool FailsWhenClosingAllSessions { get; set; } = false;

    // Simule une panne de la base pendant l'effacement des sessions expirées.
    public bool FailsDuringCleanup { get; set; } = false;

    // Simule d'autres requêtes qui changent le compte juste avant l'enregistrement de la connexion : un conflit chacune.
    public int LoginConflicts { get; set; } = 0;

    // Garde la session de côté : elle ne sera gardée qu'avec les autres changements.
    public void PrepareSession(UserSession session)
    {
        lock (pendingChangesLock)
        {
            preparedSessions.Add(session);
        }
    }

    // Garde de côté la fermeture de toutes les sessions du compte : elle ne se fera qu'avec les autres changements.
    public Task PrepareAllSessionsRemovalAsync(Guid userId)
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
    public Task<bool> DeviceBelongsToAccountAsync(string deviceTokenHash, Guid accountId)
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

    // Cherche en mémoire une session par son empreinte.
    public Task<UserSession?> FindAsync(string sessionTokenHash)
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
    public Task RecordActivityAsync(string sessionTokenHash, DateTime now)
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
    public Task DeleteAsync(string sessionTokenHash)
    {
        SavedSessions.RemoveAll(session => session.Id == sessionTokenHash);
        return Task.CompletedTask;
    }

    // Retire de la liste toutes les sessions du compte.
    public Task DeleteAllForUserAsync(Guid userId)
    {
        if (FailsWhenClosingAllSessions)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        SavedSessions.RemoveAll(session => session.UserId == userId);
        return Task.CompletedTask;
    }

    // Garde la date du passage. La vraie règle d'effacement est testée sur la vraie base.
    public Task<int> DeleteInactiveSinceAsync(DateTime activityCutoff)
    {
        if (FailsDuringCleanup)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        CleanupDates.Add(activityCutoff);
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

            SavedSessions.AddRange(preparedSessions);
            SavedDevices.AddRange(preparedDevices);
            preparedSessions.Clear();
            preparedDevices.Clear();
            accountsLosingAllSessions.Clear();
        }
    }
}
