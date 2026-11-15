using WalletFlow.Application.Notifications;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : toutes les heures, prévient l'utilisateur dont l'autorisation bancaire finit dans 7 jours (GR Comptes),
// et efface les alertes de budget et de suggestion deux mois après leur mois (SS §7). Chaque connexion a son propre accès
// à la base : celle qui échoue va au journal technique, sans arrêter les autres, et sera reprise au passage suivant.
public class AlertsBackgroundService : BackgroundService
{
    public static readonly TimeSpan PassInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider clock;
    private readonly ILogger<AlertsBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque étape, l'horloge et le journal technique.
    public AlertsBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<AlertsBackgroundService> logger)
    {
        this.scopeFactory = scopeFactory;
        this.clock = clock;
        this.logger = logger;
    }

    // Tourne tant que l'API tourne (nom imposé par le framework).
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunPassAsync();
            await Task.Delay(PassInterval, clock, stoppingToken);
        }
    }

    // Fait un passage : les deux étapes sont indépendantes, la panne de l'une n'empêche pas l'autre.
    public async Task RunPassAsync()
    {
        await WarnConnectionsAsync();
        await DeleteOldAlertsAsync();
    }

    // Prévient chaque utilisateur dont une autorisation bancaire est à renouveler.
    private async Task WarnConnectionsAsync()
    {
        List<Guid> connectionIds;

        try
        {
            connectionIds = await RunInScopeAsync<BankAuthorizationAlertService, List<Guid>>(service => service.FindConnectionsToWarnAsync());
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "La liste des autorisations bancaires à renouveler ne peut pas être lue.");
            return;
        }

        var warnedCount = 0;

        foreach (var connectionId in connectionIds)
        {
            try
            {
                if (await RunInScopeAsync<BankAuthorizationAlertService, bool>(service => service.WarnAsync(connectionId)))
                {
                    warnedCount = warnedCount + 1;
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "L'alerte d'autorisation bancaire de la connexion {IdentifiantConnexion} a échoué.", connectionId);
            }
        }

        logger.LogInformation("{NombreDAlertes} alerte(s) d'autorisation bancaire à renouveler préparée(s).", warnedCount);
    }

    // Efface les alertes de budget et de suggestion des mois passés.
    private async Task DeleteOldAlertsAsync()
    {
        try
        {
            var deletedCount = await RunInScopeAsync<AlertCleanupService, int>(service => service.DeleteOldAlertsAsync());
            logger.LogInformation("{NombreDAlertes} alerte(s) de budget ou de suggestion effacée(s).", deletedCount);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "L'effacement des vieilles alertes a échoué.");
        }
    }

    // Donne à chaque étape son propre accès à la base, oublié ensuite.
    private async Task<TResult> RunInScopeAsync<TService, TResult>(Func<TService, Task<TResult>> step)
        where TService : notnull
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();

        return await step(service);
    }
}
