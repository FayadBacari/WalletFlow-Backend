using WalletFlow.Application.UserAccounts;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : efface chaque jour les comptes non confirmés dont le lien a expiré (GR Auth, MPD §3).
public class UnconfirmedAccountsCleanupBackgroundService : RecurringBackgroundService
{
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);

    private readonly ILogger<UnconfirmedAccountsCleanupBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge, le bilan de santé des tâches et le journal technique.
    public UnconfirmedAccountsCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        BackgroundTaskHealth health,
        ILogger<UnconfirmedAccountsCleanupBackgroundService> logger)
        : base(scopeFactory, clock, CleanupInterval, health)
    {
        this.logger = logger;
    }

    // Fait un passage. Une panne ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    public override async Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var deletedCount = await RunInScopeAsync<UnconfirmedAccountsCleanupService, int>(
                cleanupService => cleanupService.DeleteExpiredUnconfirmedAccountsAsync(cancellationToken),
                cancellationToken);

            logger.LogInformation("{NombreDeComptes} compte(s) non confirmé(s) effacé(s).", deletedCount);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "L'effacement des comptes non confirmés a échoué.");
        }
    }
}
