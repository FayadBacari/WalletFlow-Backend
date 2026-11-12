using WalletFlow.Application.BankConnections;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : toutes les 15 minutes, met à jour les connexions dont la dernière mise à jour en arrière-plan
// date de 6 heures ou plus, soit 4 fois par jour au plus pour chacune (GR Comptes).
public class BankBalancesBackgroundService : BackgroundService
{
    public static readonly TimeSpan PassInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider clock;
    private readonly ILogger<BankBalancesBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge et le journal technique.
    public BankBalancesBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<BankBalancesBackgroundService> logger)
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

    // Fait un passage. Une panne ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    public async Task RunPassAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var backgroundUpdateService = scope.ServiceProvider.GetRequiredService<BankBackgroundUpdateService>();

            var updatedCount = await backgroundUpdateService.UpdateDueConnectionsAsync();

            logger.LogInformation("{NombreDeConnexions} connexion(s) bancaire(s) mise(s) à jour en arrière-plan.", updatedCount);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "La mise à jour des connexions bancaires en arrière-plan a échoué.");
        }
    }
}
