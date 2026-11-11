using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les codes de secours en mémoire.
// Les codes préparés, ou leur effacement, ne sont gardés qu'avec les autres changements (voir FakeDatabaseUnitOfWork).
public class FakeTwoFactorAuthenticationRepository : ITwoFactorAuthenticationRepository, ITransactionalFakeRepository
{
    public const string FailureDetail = "detail-technique-de-la-panne-de-la-double-authentification";

    private readonly object pendingChangesLock = new object();
    private readonly ReadCopies<BackupCode> readBackupCodes = new ReadCopies<BackupCode>();
    private readonly List<BackupCode> preparedBackupCodes = new List<BackupCode>();
    private readonly List<Guid> backupCodeRemovals = new List<Guid>();

    // Ce que la fausse base contient.
    public List<BackupCode> SavedBackupCodes { get; } = new List<BackupCode>();

    // Simule une panne de la base au moment de lire les codes de secours.
    public bool FailsWhenReadingBackupCodes { get; set; } = false;

    // Simule une panne de la base au moment d'enregistrer une activation ou une désactivation.
    public bool FailsWhenSavingSettings { get; set; } = false;

    // Les réglages qui suivent simulent une autre requête qui agit juste avant l'enregistrement de celle du test.

    // D'autres requêtes changent le compte juste avant l'enregistrement d'une activation : un conflit chacune.
    public int ActivationConflicts { get; set; } = 0;

    // D'autres requêtes changent le compte juste avant l'enregistrement d'une désactivation : un conflit chacune.
    public int DeactivationConflicts { get; set; } = 0;

    // Cherche en mémoire les codes de secours de ce compte qui n'ont pas servi.
    public Task<List<BackupCode>> FindUnusedBackupCodesAsync(Guid accountId)
    {
        if (FailsWhenReadingBackupCodes)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        var unusedCodes = new List<BackupCode>();
        foreach (var backupCode in SavedBackupCodes)
        {
            if (backupCode.UserId == accountId && backupCode.UsedAt == null)
            {
                unusedCodes.Add(backupCode);
            }
        }

        return Task.FromResult(unusedCodes);
    }

    // Cherche en mémoire un code de secours par son identifiant, et garde une copie de ce qu'il était.
    public Task<BackupCode?> FindBackupCodeAsync(Guid backupCodeId)
    {
        foreach (var backupCode in SavedBackupCodes)
        {
            if (backupCode.Id == backupCodeId)
            {
                KeepCopy(backupCode);
                return Task.FromResult<BackupCode?>(backupCode);
            }
        }

        return Task.FromResult<BackupCode?>(null);
    }

    // Garde les nouveaux codes de côté : ils ne seront gardés qu'avec les autres changements.
    public void PrepareBackupCodes(List<BackupCode> backupCodes)
    {
        lock (pendingChangesLock)
        {
            preparedBackupCodes.AddRange(backupCodes);
        }
    }

    // Note que tous les codes de ce compte devront partir avec les autres changements.
    public Task PrepareBackupCodesRemovalAsync(Guid accountId)
    {
        lock (pendingChangesLock)
        {
            backupCodeRemovals.Add(accountId);
        }

        return Task.CompletedTask;
    }

    // La panne simulée touche l'enregistrement d'une activation ou d'une désactivation.
    public void ThrowIfTableIsDown()
    {
        lock (pendingChangesLock)
        {
            if (FailsWhenSavingSettings && (preparedBackupCodes.Count > 0 || backupCodeRemovals.Count > 0))
            {
                throw new InvalidOperationException(FailureDetail);
            }
        }
    }

    // Une autre requête change le compte juste avant une activation ou une désactivation, tant que le réglage le demande.
    public bool HasConcurrentChange()
    {
        lock (pendingChangesLock)
        {
            // Des codes préparés : c'est une activation.
            if (preparedBackupCodes.Count > 0 && ActivationConflicts > 0)
            {
                ActivationConflicts = ActivationConflicts - 1;
                return true;
            }

            // Des codes à effacer : c'est une désactivation.
            if (backupCodeRemovals.Count > 0 && DeactivationConflicts > 0)
            {
                DeactivationConflicts = DeactivationConflicts - 1;
                return true;
            }

            return false;
        }
    }

    // Remet les codes lus comme ils étaient, et oublie ce qui était préparé.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readBackupCodes.RestoreAll();
            ClearTracking();
        }
    }

    // L'autre requête simulée a changé le compte, pas les codes : il n'y a rien à écrire ici.
    public void ApplyConcurrentChanges()
    {
    }

    // Garde les nouveaux codes, et efface ceux des comptes qui désactivent la double authentification.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            foreach (var accountId in backupCodeRemovals)
            {
                SavedBackupCodes.RemoveAll(backupCode => backupCode.UserId == accountId);
            }

            SavedBackupCodes.AddRange(preparedBackupCodes);
            ClearTracking();
        }
    }

    // Garde une copie du code tel qu'il est lu : il reprendra ces valeurs si l'enregistrement échoue.
    private void KeepCopy(BackupCode backupCode)
    {
        lock (pendingChangesLock)
        {
            readBackupCodes.Keep(backupCode);
        }
    }

    // Oublie les copies et ce qui était préparé : le prochain passage repart de ce qui est gardé.
    private void ClearTracking()
    {
        readBackupCodes.Clear();
        preparedBackupCodes.Clear();
        backupCodeRemovals.Clear();
    }
}
