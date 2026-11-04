using WalletFlow.Application.UserAccounts;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : efface chaque jour les comptes non confirmés dont le lien a expiré (GR Auth, MPD §3).
public class UnconfirmedAccountsCleanupBackgroundService : BackgroundService
{
    public static readonly TimeSpan DurationBetweenTwoCleanupsOfTheAccounts = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory factoryOfTheScopesUsedByTheCleanups;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;
    private readonly ILogger<UnconfirmedAccountsCleanupBackgroundService> loggerOfTheCleanupsOfTheAccounts;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge et le journal technique.
    public UnconfirmedAccountsCleanupBackgroundService(
        IServiceScopeFactory factoryOfTheScopesUsedByTheCleanups,
        TimeProvider clockThatGivesTheCurrentDateAndTime,
        ILogger<UnconfirmedAccountsCleanupBackgroundService> loggerOfTheCleanupsOfTheAccounts)
    {
        this.factoryOfTheScopesUsedByTheCleanups = factoryOfTheScopesUsedByTheCleanups;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
        this.loggerOfTheCleanupsOfTheAccounts = loggerOfTheCleanupsOfTheAccounts;
    }

    // Tourne tant que l'API tourne (nom imposé par le framework).
    protected override async Task ExecuteAsync(CancellationToken tokenThatStopsTheCleanupTask)
    {
        while (!tokenThatStopsTheCleanupTask.IsCancellationRequested)
        {
            await RunOneCleanupOfTheUnconfirmedAccountsAsync();
            await Task.Delay(DurationBetweenTwoCleanupsOfTheAccounts, clockThatGivesTheCurrentDateAndTime, tokenThatStopsTheCleanupTask);
        }
    }

    // Fait un passage. Une panne ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    public async Task RunOneCleanupOfTheUnconfirmedAccountsAsync()
    {
        try
        {
            using var scopeOfThisCleanupOfTheAccounts = factoryOfTheScopesUsedByTheCleanups.CreateScope();
            var serviceThatDeletesTheUnconfirmedAccounts = scopeOfThisCleanupOfTheAccounts.ServiceProvider.GetRequiredService<UnconfirmedAccountsCleanupService>();

            var numberOfAccountsDeletedByThisCleanup = await serviceThatDeletesTheUnconfirmedAccounts.DeleteUnconfirmedAccountsWithExpiredLinkAsync();

            loggerOfTheCleanupsOfTheAccounts.LogInformation("{NombreDeComptes} compte(s) non confirmé(s) effacé(s).", numberOfAccountsDeletedByThisCleanup);
        }
        catch (Exception technicalErrorRaisedByTheCleanup)
        {
            loggerOfTheCleanupsOfTheAccounts.LogError(technicalErrorRaisedByTheCleanup, "L'effacement des comptes non confirmés a échoué.");
        }
    }
}
