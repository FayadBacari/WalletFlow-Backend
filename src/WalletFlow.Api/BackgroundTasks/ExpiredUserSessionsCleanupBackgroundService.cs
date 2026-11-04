using WalletFlow.Application.UserSessions;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : efface chaque jour les sessions expirées (MPD §3).
public class ExpiredUserSessionsCleanupBackgroundService : BackgroundService
{
    public static readonly TimeSpan DurationBetweenTwoCleanupsOfTheSessions = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory factoryOfTheScopesUsedByTheCleanups;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;
    private readonly ILogger<ExpiredUserSessionsCleanupBackgroundService> loggerOfTheCleanupsOfTheSessions;

    // Reçoit de quoi créer un accès à la base pour chaque passage, l'horloge et le journal technique.
    public ExpiredUserSessionsCleanupBackgroundService(
        IServiceScopeFactory factoryOfTheScopesUsedByTheCleanups,
        TimeProvider clockThatGivesTheCurrentDateAndTime,
        ILogger<ExpiredUserSessionsCleanupBackgroundService> loggerOfTheCleanupsOfTheSessions)
    {
        this.factoryOfTheScopesUsedByTheCleanups = factoryOfTheScopesUsedByTheCleanups;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
        this.loggerOfTheCleanupsOfTheSessions = loggerOfTheCleanupsOfTheSessions;
    }

    // Tourne tant que l'API tourne (nom imposé par le framework).
    protected override async Task ExecuteAsync(CancellationToken tokenThatStopsTheCleanupTask)
    {
        while (!tokenThatStopsTheCleanupTask.IsCancellationRequested)
        {
            await RunOneCleanupOfTheExpiredUserSessionsAsync();
            await Task.Delay(DurationBetweenTwoCleanupsOfTheSessions, clockThatGivesTheCurrentDateAndTime, tokenThatStopsTheCleanupTask);
        }
    }

    // Fait un passage. Une panne ne doit pas arrêter l'API : la tâche réessaiera au prochain passage.
    public async Task RunOneCleanupOfTheExpiredUserSessionsAsync()
    {
        try
        {
            using var scopeOfThisCleanupOfTheSessions = factoryOfTheScopesUsedByTheCleanups.CreateScope();
            var serviceThatDeletesTheExpiredSessions = scopeOfThisCleanupOfTheSessions.ServiceProvider.GetRequiredService<ExpiredUserSessionsCleanupService>();

            var numberOfSessionsDeletedByThisCleanup = await serviceThatDeletesTheExpiredSessions.DeleteExpiredUserSessionsFromTheDatabaseAsync();

            loggerOfTheCleanupsOfTheSessions.LogInformation("{NombreDeSessions} session(s) expirée(s) effacée(s).", numberOfSessionsDeletedByThisCleanup);
        }
        catch (Exception technicalErrorRaisedByTheCleanup)
        {
            loggerOfTheCleanupsOfTheSessions.LogError(technicalErrorRaisedByTheCleanup, "L'effacement des sessions expirées a échoué.");
        }
    }
}
