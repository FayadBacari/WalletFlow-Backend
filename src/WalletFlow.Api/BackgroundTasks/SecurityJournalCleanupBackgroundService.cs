using WalletFlow.Application.Security;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : efface chaque jour les événements du journal de sécurité de plus d'un an (SS §11).
public class SecurityJournalCleanupBackgroundService : BackgroundService
{
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider clock;
    private readonly ILogger<SecurityJournalCleanupBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge et le journal technique.
    public SecurityJournalCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<SecurityJournalCleanupBackgroundService> logger)
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
            await RunCleanupAsync();
            await Task.Delay(CleanupInterval, clock, stoppingToken);
        }
    }

    // Fait un passage. Une panne ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    public async Task RunCleanupAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var cleanupService = scope.ServiceProvider.GetRequiredService<SecurityJournalCleanupService>();
            var deletedCount = await cleanupService.DeleteExpiredEventsAsync();
            logger.LogInformation("{NombreDEvenements} événement(s) de plus d'un an effacé(s) du journal de sécurité.", deletedCount);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "L'effacement des vieux événements du journal de sécurité a échoué.");
        }
    }
}
