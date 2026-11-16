using WalletFlow.Application.Notifications;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les 10 types d'alerte, les choix des utilisateurs et les alertes envoyées
// en mémoire. Ce qui est préparé n'est gardé qu'avec les autres changements (voir FakeDatabaseUnitOfWork). Comme les index
// uniques de la vraie base, une alerte gardée deux fois fait échouer l'enregistrement : un cas d'utilisation qui la
// préparerait deux fois est en faute, et le test le voit. Les noms visés par un e-mail se lisent dans les faux dépôts
// des catégories, des sacs et des connexions que le test relie. Les requêtes SQL elles-mêmes sont testées sur la vraie
// base (AlertRepositoryTests).
public class FakeAlertRepository : IAlertRepository, ITransactionalFakeRepository
{
    public const string FailureDetail = "detail-technique-de-la-panne-des-alertes";

    private readonly object pendingChangesLock = new object();
    private readonly List<AlertPreference> preparedPreferences = new List<AlertPreference>();
    private readonly List<Alert> preparedAlerts = new List<Alert>();
    private readonly List<Alert> preparedDeletions = new List<Alert>();
    private readonly ReadCopies<AlertPreference> readPreferences = new ReadCopies<AlertPreference>();
    private Action? concurrentChanges = null;

    // Les 10 types d'alerte, comme le script SQL les remplit.
    public List<AlertType> SavedTypes { get; } = new List<AlertType>
    {
        new AlertType { Kind = AlertKind.BudgetReached, Label = "Budget d'une catégorie atteint", CanBeDisabled = true },
        new AlertType { Kind = AlertKind.NewSuggestion, Label = "Nouvelle suggestion d'épargne", CanBeDisabled = true },
        new AlertType { Kind = AlertKind.BagGoalReached, Label = "Objectif d'un sac atteint", CanBeDisabled = true },
        new AlertType { Kind = AlertKind.BankAuthorizationToRenew, Label = "Autorisation bancaire à renouveler", CanBeDisabled = true },
        new AlertType { Kind = AlertKind.NewDevice, Label = "Connexion depuis un nouvel appareil", CanBeDisabled = false },
        new AlertType { Kind = AlertKind.AccountLocked, Label = "Compte bloqué", CanBeDisabled = false },
        new AlertType { Kind = AlertKind.PasswordChange, Label = "Changement de mot de passe", CanBeDisabled = false },
        new AlertType { Kind = AlertKind.EmailChange, Label = "Changement d'adresse e-mail", CanBeDisabled = false },
        new AlertType { Kind = AlertKind.TwoFactorDeactivation, Label = "Désactivation de la double authentification", CanBeDisabled = false },
        new AlertType { Kind = AlertKind.BackupCodeUse, Label = "Utilisation d'un code de secours", CanBeDisabled = false },
    };

    // Ce que la fausse base contient.
    public List<AlertPreference> SavedPreferences { get; } = new List<AlertPreference>();
    public List<Alert> SavedAlerts { get; } = new List<Alert>();

    // Le nombre d'alertes ajoutées par chaque enregistrement réussi qui en ajoute.
    public List<int> AlertsPerCommit { get; } = new List<int>();

    // Les faux dépôts où se lisent les noms et les dates de fin que disent les e-mails, et les connexions à prévenir.
    public FakeCategoryRepository? Categories { get; set; } = null;
    public FakeBagRepository? Bags { get; set; } = null;
    public FakeBankConnectionRepository? Connections { get; set; } = null;

    // Simule une panne de la base : lire ou écrire les alertes échoue.
    public bool IsDown { get; set; } = false;

    // Simule une autre requête qui enregistre cette alerte juste avant le prochain enregistrement qui en prépare une :
    // comme l'index unique de la vraie base, l'enregistrement en cours échoue, et le passage suivant la trouve déjà partie.
    public Alert? AlertSavedBeforeSave { get; set; } = null;

    // Simule une autre requête qui enregistre ce choix juste avant le prochain enregistrement qui en crée un :
    // comme la clé primaire de la vraie base, l'enregistrement en cours échoue, et le passage suivant le relit.
    public AlertPreference? PreferenceSavedBeforeSave { get; set; } = null;

    // Simule d'autres requêtes qui écrivent les mêmes lignes juste avant chaque enregistrement : un conflit chacune.
    public int Conflicts { get; set; } = 0;

    // Donne les 10 types d'alerte.
    public Task<List<AlertType>> FindTypesAsync()
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            return Task.FromResult(new List<AlertType>(SavedTypes));
        }
    }

    // Donne des copies des choix gardés de l'utilisateur : rien n'est suivi.
    public Task<List<AlertPreference>> FindPreferencesAsync(Guid userId)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var preferences = SavedPreferences
                .Where(preference => preference.UserId == userId)
                .Select(preference => EntityValues.Copy(preference))
                .ToList();

            return Task.FromResult(preferences);
        }
    }

    // Cherche le choix gardé de l'utilisateur pour ce type, et garde une copie de ses valeurs.
    public Task<AlertPreference?> FindPreferenceAsync(Guid userId, AlertKind kind)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var preference = SavedPreferences.SingleOrDefault(saved => saved.UserId == userId && saved.Kind == kind);

            if (preference != null)
            {
                readPreferences.Keep(preference);
            }

            return Task.FromResult(preference);
        }
    }

    // Un type est activé tant qu'aucun choix gardé ne le désactive.
    public Task<bool> IsEnabledAsync(Guid userId, AlertKind kind)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var isDisabled = SavedPreferences.Any(preference =>
                preference.UserId == userId && preference.Kind == kind && !preference.IsEnabled);

            return Task.FromResult(!isDisabled);
        }
    }

    // Donne des copies des alertes de budget gardées pour ce mois : rien n'est suivi.
    public Task<List<Alert>> FindBudgetAlertsAsync(Guid userId, DateOnly month)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var alerts = SavedAlerts
                .Where(alert => alert.UserId == userId && alert.Kind == AlertKind.BudgetReached && alert.Month == month)
                .Select(alert => EntityValues.Copy(alert))
                .ToList();

            return Task.FromResult(alerts);
        }
    }

    // Cherche l'alerte d'objectif gardée pour ce sac.
    public Task<Alert?> FindBagAlertAsync(Guid bagId)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            return Task.FromResult(SavedAlerts.SingleOrDefault(alert => alert.BagId == bagId && alert.Kind == AlertKind.BagGoalReached));
        }
    }

    // Cherche l'alerte d'autorisation gardée pour cette connexion.
    public Task<Alert?> FindConnectionAlertAsync(Guid connectionId)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            return Task.FromResult(SavedAlerts.SingleOrDefault(alert =>
                alert.ConnectionId == connectionId && alert.Kind == AlertKind.BankAuthorizationToRenew));
        }
    }

    // Cherche, dans le faux dépôt des connexions, celles à prévenir, comme la vraie requête.
    public Task<List<Guid>> FindConnectionsToWarnAsync(DateTime authorizationEndLimit)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var connectionIds = ReadConnections().SavedConnections
                .Where(connection =>
                    connection.AuthorizationExpiresAt <= authorizationEndLimit
                    && !SavedAlerts.Any(alert => alert.ConnectionId == connection.Id && alert.Kind == AlertKind.BankAuthorizationToRenew)
                    && !SavedPreferences.Any(preference =>
                        preference.UserId == connection.UserId
                        && preference.Kind == AlertKind.BankAuthorizationToRenew
                        && !preference.IsEnabled))
                .Select(connection => connection.Id)
                .ToList();

            return Task.FromResult(connectionIds);
        }
    }

    // Lit l'alerte gardée, puis le nom de ce qu'elle vise dans les faux dépôts reliés.
    public Task<AlertEmailContent?> ReadEmailContentAsync(Guid alertId)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var alert = SavedAlerts.SingleOrDefault(saved => saved.Id == alertId);

            if (alert == null)
            {
                return Task.FromResult<AlertEmailContent?>(null);
            }

            return Task.FromResult(CreateContent(alert));
        }
    }

    // Garde le choix de côté : il ne sera gardé qu'avec les autres changements.
    public void PreparePreference(AlertPreference preference)
    {
        lock (pendingChangesLock)
        {
            preparedPreferences.Add(preference);
        }
    }

    // Garde l'alerte de côté : elle ne sera gardée qu'avec les autres changements.
    public void PrepareAlert(Alert alert)
    {
        lock (pendingChangesLock)
        {
            preparedAlerts.Add(alert);
        }
    }

    // Garde l'effacement de côté : il ne sera fait qu'avec les autres changements.
    public void PrepareDeletion(Alert alert)
    {
        lock (pendingChangesLock)
        {
            preparedDeletions.Add(alert);
        }
    }

    // Efface tout de suite les alertes de budget et de suggestion des mois d'avant celui-ci.
    public Task<int> DeleteMonthAlertsBeforeAsync(DateOnly firstKeptMonth)
    {
        ThrowIfDown();

        lock (pendingChangesLock)
        {
            var deletedCount = SavedAlerts.RemoveAll(alert => alert.Month < firstKeptMonth);

            return Task.FromResult(deletedCount);
        }
    }

    // La panne touche tout ce que l'enregistrement écrit dans les alertes.
    public void ThrowIfTableIsDown()
    {
        lock (pendingChangesLock)
        {
            if (IsDown && (preparedAlerts.Count > 0 || preparedDeletions.Count > 0 || preparedPreferences.Count > 0))
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
            var switchedPreference = readPreferences.FindChangedSinceRead((copy, current) => copy.IsEnabled != current.IsEnabled);
            var isWriting = preparedAlerts.Count > 0 || preparedDeletions.Count > 0 || preparedPreferences.Count > 0
                || switchedPreference != null;

            var otherAlert = AlertSavedBeforeSave;
            if (otherAlert != null && preparedAlerts.Count > 0)
            {
                AlertSavedBeforeSave = null;
                concurrentChanges = () => SavedAlerts.Add(otherAlert);
                return true;
            }

            var otherPreference = PreferenceSavedBeforeSave;
            if (otherPreference != null && preparedPreferences.Count > 0)
            {
                PreferenceSavedBeforeSave = null;
                concurrentChanges = () => SavedPreferences.Add(otherPreference);
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

    // Remet les choix lus comme ils étaient, et oublie ce qui a été préparé.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readPreferences.RestoreAll();
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

    // Fait les effacements préparés, puis garde les alertes et les choix préparés. Comme la vraie base, une alerte qui
    // existe déjà pour la même catégorie, le même mois et le même seuil, le même mois de suggestions, le même sac ou la même
    // connexion, ou un second choix pour le même type, fait échouer l'enregistrement.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            foreach (var deleted in preparedDeletions)
            {
                SavedAlerts.Remove(deleted);
            }

            if (preparedAlerts.Count > 0)
            {
                AlertsPerCommit.Add(preparedAlerts.Count);
            }

            foreach (var alert in preparedAlerts)
            {
                if (SavedAlerts.Any(saved => IsSameAlert(saved, alert)))
                {
                    throw new InvalidOperationException("Une alerte déjà gardée a été préparée une seconde fois.");
                }

                SavedAlerts.Add(alert);
            }

            foreach (var preference in preparedPreferences)
            {
                if (SavedPreferences.Any(saved => saved.UserId == preference.UserId && saved.Kind == preference.Kind))
                {
                    throw new InvalidOperationException("Un choix déjà gardé a été préparé une seconde fois.");
                }

                SavedPreferences.Add(preference);
            }

            ClearTracking();
        }
    }

    // Donne les alertes gardées de cet utilisateur, pour vérifier ce que la base contient.
    public List<Alert> AlertsOf(Guid userId)
    {
        lock (pendingChangesLock)
        {
            return SavedAlerts.Where(alert => alert.UserId == userId).ToList();
        }
    }

    // Renvoie vrai si les deux alertes tombent sous le même index unique de la vraie base.
    private static bool IsSameAlert(Alert saved, Alert alert)
    {
        if (saved.Kind != alert.Kind)
        {
            return false;
        }

        return alert.Kind switch
        {
            AlertKind.BudgetReached => saved.CategoryId == alert.CategoryId && saved.Month == alert.Month && saved.Threshold == alert.Threshold,
            AlertKind.NewSuggestion => saved.UserId == alert.UserId && saved.Month == alert.Month,
            AlertKind.BagGoalReached => saved.BagId == alert.BagId,
            AlertKind.BankAuthorizationToRenew => saved.ConnectionId == alert.ConnectionId,
            _ => throw new InvalidOperationException("Ce type d'alerte n'est jamais gardé.")
        };
    }

    // Crée ce que dit l'e-mail de l'alerte, avec les noms gardés dans les faux dépôts reliés.
    private AlertEmailContent? CreateContent(Alert alert)
    {
        if (alert.Kind == AlertKind.BudgetReached && alert.Month is DateOnly budgetMonth && alert.Threshold is short threshold)
        {
            var category = Categories?.SavedCategories.SingleOrDefault(saved => saved.Id == alert.CategoryId);

            return category == null
                ? null
                : new AlertEmailContent.BudgetReached { CategoryName = category.Name, Month = budgetMonth, Threshold = threshold };
        }

        if (alert.Kind == AlertKind.NewSuggestion && alert.Month is DateOnly suggestionMonth)
        {
            return new AlertEmailContent.NewSuggestions { Month = suggestionMonth };
        }

        if (alert.Kind == AlertKind.BagGoalReached)
        {
            var bag = Bags?.SavedBags.SingleOrDefault(saved => saved.Id == alert.BagId);

            return bag == null ? null : new AlertEmailContent.BagGoalReached { BagName = bag.Name };
        }

        if (alert.Kind == AlertKind.BankAuthorizationToRenew)
        {
            var connection = Connections?.SavedConnections.SingleOrDefault(saved => saved.Id == alert.ConnectionId);

            return connection == null
                ? null
                : new AlertEmailContent.BankAuthorizationToRenew
                {
                    BankName = connection.BankName,
                    AuthorizationExpiresAt = connection.AuthorizationExpiresAt
                };
        }

        throw new InvalidOperationException("Cette alerte n'a pas ce que son type demande.");
    }

    // Le faux dépôt des connexions relié par le test.
    private FakeBankConnectionRepository ReadConnections()
    {
        return Connections ?? throw new InvalidOperationException("Le test n'a relié aucun faux dépôt des connexions.");
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
        preparedPreferences.Clear();
        preparedAlerts.Clear();
        preparedDeletions.Clear();
        readPreferences.Clear();
    }
}
