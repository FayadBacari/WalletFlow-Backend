using WalletFlow.Application.BankConnections;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : toutes les 15 minutes, met à jour les connexions dont la dernière mise à jour en arrière-plan
// date de 6 heures ou plus, soit 4 fois par jour au plus pour chacune (GR Comptes).
public class BankBalancesBackgroundService : RecurringBackgroundService
{
    public static readonly TimeSpan PassInterval = TimeSpan.FromMinutes(15);

    private readonly ILogger<BankBalancesBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge, le bilan de santé des tâches et le journal technique.
    public BankBalancesBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        BackgroundTaskHealth health,
        ILogger<BankBalancesBackgroundService> logger)
        : base(scopeFactory, clock, PassInterval, health)
    {
        this.logger = logger;
    }

    // Fait un passage. Une panne ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    public override async Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var updatedCount = await RunInScopeAsync<BankBackgroundUpdateService, int>(
                backgroundUpdateService => backgroundUpdateService.UpdateDueConnectionsAsync(cancellationToken),
                cancellationToken);

            logger.LogInformation("{NombreDeConnexions} connexion(s) bancaire(s) mise(s) à jour en arrière-plan.", updatedCount);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "La mise à jour des connexions bancaires en arrière-plan a échoué.");
        }
    }
}
