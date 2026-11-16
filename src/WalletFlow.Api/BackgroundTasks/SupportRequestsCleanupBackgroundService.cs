using WalletFlow.Application.Support;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : efface chaque jour les demandes de support envoyées il y a plus d'un an, avec leurs réponses (GR Admin, SS §7).
public class SupportRequestsCleanupBackgroundService : BackgroundService
{
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider clock;
    private readonly ILogger<SupportRequestsCleanupBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge et le journal technique.
    public SupportRequestsCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<SupportRequestsCleanupBackgroundService> logger)
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
            var cleanupService = scope.ServiceProvider.GetRequiredService<SupportRequestCleanupService>();
            var deletedCount = await cleanupService.DeleteOldRequestsAsync();
            logger.LogInformation("{NombreDeDemandes} demande(s) de support de plus d'un an effacée(s).", deletedCount);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "L'effacement des vieilles demandes de support a échoué.");
        }
    }
}
