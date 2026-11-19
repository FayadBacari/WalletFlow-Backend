using WalletFlow.Application.Security;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : efface chaque jour les événements du journal de sécurité de plus d'un an (SS §11).
public class SecurityJournalCleanupBackgroundService : RecurringBackgroundService
{
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);

    private readonly ILogger<SecurityJournalCleanupBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge, le bilan de santé des tâches et le journal technique.
    public SecurityJournalCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        BackgroundTaskHealth health,
        ILogger<SecurityJournalCleanupBackgroundService> logger)
        : base(scopeFactory, clock, CleanupInterval, health)
    {
        this.logger = logger;
    }

    // Fait un passage. Une panne ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    public override async Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var deletedCount = await RunInScopeAsync<SecurityJournalCleanupService, int>(
                cleanupService => cleanupService.DeleteExpiredEventsAsync(cancellationToken),
                cancellationToken);
            logger.LogInformation("{NombreDEvenements} événement(s) de plus d'un an effacé(s) du journal de sécurité.", deletedCount);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "L'effacement des vieux événements du journal de sécurité a échoué.");
        }
    }
}
