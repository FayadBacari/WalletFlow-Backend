using WalletFlow.Application.Notifications;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : toutes les heures, prévient l'utilisateur dont l'autorisation bancaire finit dans 7 jours (GR Comptes),
// et efface les alertes de budget et de suggestion deux mois après leur mois (SS §7). Chaque connexion a son propre accès
// à la base : celle qui échoue va au journal technique, sans arrêter les autres, et sera reprise au passage suivant.
public class AlertsBackgroundService : RecurringBackgroundService
{
    public static readonly TimeSpan PassInterval = TimeSpan.FromHours(1);

    private readonly ILogger<AlertsBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque étape, l'horloge, le bilan de santé des tâches et le journal technique.
    public AlertsBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        BackgroundTaskHealth health,
        ILogger<AlertsBackgroundService> logger)
        : base(scopeFactory, clock, PassInterval, health)
    {
        this.logger = logger;
    }

    // Fait un passage : les deux étapes sont indépendantes, la panne de l'une n'empêche pas l'autre.
    public override async Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        await WarnConnectionsAsync(cancellationToken);
        await DeleteOldAlertsAsync(cancellationToken);
    }

    // Prévient chaque utilisateur dont une autorisation bancaire est à renouveler.
    private async Task WarnConnectionsAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> connectionIds;

        try
        {
            connectionIds = await RunInScopeAsync<BankAuthorizationAlertService, List<Guid>>(service => service.FindConnectionsToWarnAsync(cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "La liste des autorisations bancaires à renouveler ne peut pas être lue.");
            return;
        }

        var warnedCount = 0;

        foreach (var connectionId in connectionIds)
        {
            try
            {
                if (await RunInScopeAsync<BankAuthorizationAlertService, bool>(service => service.WarnAsync(connectionId, cancellationToken), cancellationToken))
                {
                    warnedCount = warnedCount + 1;
                }
            }
            catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
            {
                logger.LogError(exception, "L'alerte d'autorisation bancaire de la connexion {IdentifiantConnexion} a échoué.", connectionId);
            }
        }

        logger.LogInformation("{NombreDAlertes} alerte(s) d'autorisation bancaire à renouveler préparée(s).", warnedCount);
    }

    // Efface les alertes de budget et de suggestion des mois passés.
    private async Task DeleteOldAlertsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var deletedCount = await RunInScopeAsync<AlertCleanupService, int>(service => service.DeleteOldAlertsAsync(cancellationToken), cancellationToken);
            logger.LogInformation("{NombreDAlertes} alerte(s) de budget ou de suggestion effacée(s).", deletedCount);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "L'effacement des vieilles alertes a échoué.");
        }
    }
}
