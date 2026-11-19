using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Notifications;

// Accès aux types d'alerte, aux choix des utilisateurs et aux alertes envoyées. Implémenté dans la couche Infrastructure.
// Les objets lus sont suivis, sauf mention contraire, et les méthodes « Prepare… » n'écrivent rien (voir IDatabaseUnitOfWork).
public interface IAlertRepository
{
    // Donne les 10 types d'alerte. Rien n'est suivi.
    Task<List<AlertType>> FindTypesAsync(CancellationToken cancellationToken = default);

    // Donne les choix enregistrés de l'utilisateur. Un type sans choix enregistré est activé. Rien n'est suivi.
    Task<List<AlertPreference>> FindPreferencesAsync(Guid userId, CancellationToken cancellationToken = default);

    // Cherche le choix de l'utilisateur pour ce type d'alerte. Renvoie null s'il n'en a pas enregistré.
    Task<AlertPreference?> FindPreferenceAsync(Guid userId, AlertKind kind, CancellationToken cancellationToken = default);

    // Renvoie vrai si l'utilisateur n'a pas désactivé ce type d'alerte.
    Task<bool> IsEnabledAsync(Guid userId, AlertKind kind, CancellationToken cancellationToken = default);

    // Donne les alertes de budget de l'utilisateur pour ce mois. Rien n'est suivi.
    Task<List<Alert>> FindBudgetAlertsAsync(Guid userId, DateOnly month, CancellationToken cancellationToken = default);

    // Cherche l'alerte d'objectif de ce sac. Renvoie null s'il n'en a pas.
    Task<Alert?> FindBagAlertAsync(Guid bagId, CancellationToken cancellationToken = default);

    // Cherche l'alerte d'autorisation de cette connexion bancaire. Renvoie null si elle n'en a pas.
    Task<Alert?> FindConnectionAlertAsync(Guid connectionId, CancellationToken cancellationToken = default);

    // Donne les connexions de tous les utilisateurs dont l'autorisation finit à cette date ou avant, qui n'ont pas encore
    // leur alerte, et dont l'utilisateur n'a pas désactivé cette alerte ni demandé la suppression de son compte : la tâche de
    // fond les prévient. Rien n'est suivi.
    Task<List<Guid>> FindConnectionsToWarnAsync(DateTime authorizationEndLimit, CancellationToken cancellationToken = default);

    // Donne ce que dit l'e-mail de cette alerte, lu au moment de l'envoi. Renvoie null si l'alerte n'existe plus. Rien n'est suivi.
    Task<AlertEmailContent?> ReadEmailContentAsync(Guid alertId, CancellationToken cancellationToken = default);

    // Garde de côté un nouveau choix : il ne sera enregistré qu'avec les autres changements.
    void PreparePreference(AlertPreference preference);

    // Garde de côté une nouvelle alerte : elle ne sera enregistrée qu'avec les autres changements.
    void PrepareAlert(Alert alert);

    // Prépare l'effacement de cette alerte, lue par FindBagAlertAsync ou FindConnectionAlertAsync. Son e-mail, s'il attend
    // encore dans la file, part avec elle (ON DELETE CASCADE).
    void PrepareDeletion(Alert alert);

    // Efface les alertes de budget et de suggestion des mois d'avant celui-ci, et renvoie leur nombre (SS §7).
    // Cette écriture est immédiate.
    Task<int> DeleteMonthAlertsBeforeAsync(DateOnly firstKeptMonth, CancellationToken cancellationToken = default);
}
