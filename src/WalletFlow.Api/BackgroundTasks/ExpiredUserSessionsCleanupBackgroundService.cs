using WalletFlow.Application.UserSessions;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : efface chaque jour les sessions expirées et les appareils oubliés (MPD §3, SS §3).
public class ExpiredUserSessionsCleanupBackgroundService : RecurringBackgroundService
{
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);

    private readonly ILogger<ExpiredUserSessionsCleanupBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge, le bilan de santé des tâches et le journal technique.
    public ExpiredUserSessionsCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        BackgroundTaskHealth health,
        ILogger<ExpiredUserSessionsCleanupBackgroundService> logger)
        : base(scopeFactory, clock, CleanupInterval, health)
    {
        this.logger = logger;
    }

    // Fait un passage. Une panne ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    public override async Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (deletedCount, forgottenDeviceCount) = await RunInScopeAsync<ExpiredUserSessionsCleanupService, (int, int)>(
                async cleanupService => (
                    await cleanupService.DeleteExpiredSessionsAsync(cancellationToken),
                    await cleanupService.DeleteForgottenDevicesAsync(cancellationToken)),
                cancellationToken);

            logger.LogInformation(
                "{NombreDeSessions} session(s) expirée(s) effacée(s), {NombreDAppareils} appareil(s) oublié(s).",
                deletedCount,
                forgottenDeviceCount);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "L'effacement des sessions expirées a échoué.");
        }
    }
}
