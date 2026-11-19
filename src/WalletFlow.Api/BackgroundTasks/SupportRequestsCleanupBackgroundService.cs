using WalletFlow.Application.Support;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : efface chaque jour les demandes de support envoyées il y a plus d'un an, avec leurs réponses (GR Admin, SS §7).
public class SupportRequestsCleanupBackgroundService : RecurringBackgroundService
{
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);

    private readonly ILogger<SupportRequestsCleanupBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge, le bilan de santé des tâches et le journal technique.
    public SupportRequestsCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        BackgroundTaskHealth health,
        ILogger<SupportRequestsCleanupBackgroundService> logger)
        : base(scopeFactory, clock, CleanupInterval, health)
    {
        this.logger = logger;
    }

    // Fait un passage. Une panne ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    public override async Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var deletedCount = await RunInScopeAsync<SupportRequestCleanupService, int>(
                cleanupService => cleanupService.DeleteOldRequestsAsync(cancellationToken),
                cancellationToken);
            logger.LogInformation("{NombreDeDemandes} demande(s) de support de plus d'un an effacée(s).", deletedCount);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "L'effacement des vieilles demandes de support a échoué.");
        }
    }
}
