using WalletFlow.Application.Suggestions;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : toutes les heures, calcule les suggestions du mois évalué de chaque utilisateur qui les attend (GR Sacs),
// à partir du 5 du mois suivant, avec l'alerte « nouvelle suggestion ». Chaque calcul a son propre accès à la base :
// celui qui échoue va au journal technique, sans arrêter les autres, et sera refait au passage suivant.
public class SuggestionsBackgroundService : BackgroundService
{
    public static readonly TimeSpan PassInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider clock;
    private readonly ILogger<SuggestionsBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque étape, l'horloge et le journal technique.
    public SuggestionsBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<SuggestionsBackgroundService> logger)
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

    // Fait un passage : parcourt les utilisateurs qui attendent leur calcul, par paquets, dans l'ordre de leur identifiant.
    // Le journal désigne l'utilisateur par son identifiant, jamais par son adresse (SS §7).
    public async Task RunPassAsync()
    {
        DateOnly month;
        Guid? lastUserId = null;
        var calculatedCount = 0;

        try
        {
            month = await RunInScopeAsync(service => Task.FromResult(service.FindEvaluatedMonth()));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Le mois des suggestions à calculer ne peut pas être lu.");
            return;
        }

        while (true)
        {
            List<Guid> userIds;

            try
            {
                userIds = await RunInScopeAsync(service =>
                    service.FindUsersToCalculateAsync(month, lastUserId, SuggestionCalculationService.UserBatchSize));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "La liste des suggestions à calculer ne peut pas être lue.");
                return;
            }

            if (userIds.Count == 0)
            {
                break;
            }

            foreach (var userId in userIds)
            {
                if (await TryCalculateAsync(userId, month))
                {
                    calculatedCount = calculatedCount + 1;
                }
            }

            lastUserId = userIds[userIds.Count - 1];
        }

        logger.LogInformation("{NombreDUtilisateurs} calcul(s) de suggestions fait(s) en arrière-plan.", calculatedCount);
    }

    // Calcule le mois de cet utilisateur. Une panne va au journal technique, et le passage continue avec le suivant.
    private async Task<bool> TryCalculateAsync(Guid userId, DateOnly month)
    {
        try
        {
            return await RunInScopeAsync(service => service.CalculateAsync(userId, month));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Le calcul des suggestions de l'utilisateur {IdentifiantUtilisateur} a échoué.", userId);
            return false;
        }
    }

    // Donne à chaque étape son propre accès à la base, oublié ensuite.
    private async Task<TResult> RunInScopeAsync<TResult>(Func<SuggestionCalculationService, Task<TResult>> step)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var calculationService = scope.ServiceProvider.GetRequiredService<SuggestionCalculationService>();

        return await step(calculationService);
    }
}
