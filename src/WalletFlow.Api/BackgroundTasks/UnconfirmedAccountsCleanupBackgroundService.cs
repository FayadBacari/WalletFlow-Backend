using WalletFlow.Application.UserAccounts;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : efface chaque jour les comptes non confirmés dont le lien a expiré (GR Auth, MPD §3).
public class UnconfirmedAccountsCleanupBackgroundService : BackgroundService
{
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider clock;
    private readonly ILogger<UnconfirmedAccountsCleanupBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge et le journal technique.
    public UnconfirmedAccountsCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<UnconfirmedAccountsCleanupBackgroundService> logger)
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
            var cleanupService = scope.ServiceProvider.GetRequiredService<UnconfirmedAccountsCleanupService>();

            var deletedCount = await cleanupService.DeleteExpiredUnconfirmedAccountsAsync();

            logger.LogInformation("{NombreDeComptes} compte(s) non confirmé(s) effacé(s).", deletedCount);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "L'effacement des comptes non confirmés a échoué.");
        }
    }
}
