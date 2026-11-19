using WalletFlow.Application.Suggestions;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : toutes les heures, calcule les suggestions du mois évalué de chaque utilisateur qui les attend (GR Sacs),
// à partir du 5 du mois suivant, avec l'alerte « nouvelle suggestion ». Chaque calcul a son propre accès à la base :
// celui qui échoue va au journal technique, sans arrêter les autres, et sera refait au passage suivant.
public class SuggestionsBackgroundService : RecurringBackgroundService
{
    public static readonly TimeSpan PassInterval = TimeSpan.FromHours(1);

    private readonly ILogger<SuggestionsBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque étape, l'horloge, le bilan de santé des tâches et le journal technique.
    public SuggestionsBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        BackgroundTaskHealth health,
        ILogger<SuggestionsBackgroundService> logger)
        : base(scopeFactory, clock, PassInterval, health)
    {
        this.logger = logger;
    }

    // Fait un passage : parcourt les utilisateurs qui attendent leur calcul, par paquets, dans l'ordre de leur identifiant.
    // Le journal désigne l'utilisateur par son identifiant, jamais par son adresse (SS §7).
    public override async Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        DateOnly month;
        Guid? lastUserId = null;
        var calculatedCount = 0;

        try
        {
            month = await RunInScopeAsync<SuggestionCalculationService, DateOnly>(service => Task.FromResult(service.FindEvaluatedMonth()), cancellationToken);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "Le mois des suggestions à calculer ne peut pas être lu.");
            return;
        }

        while (true)
        {
            List<Guid> userIds;

            try
            {
                userIds = await RunInScopeAsync<SuggestionCalculationService, List<Guid>>(service =>
                    service.FindUsersToCalculateAsync(month, lastUserId, SuggestionCalculationService.UserBatchSize, cancellationToken),
                    cancellationToken);
            }
            catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
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
                if (await TryCalculateAsync(userId, month, cancellationToken))
                {
                    calculatedCount = calculatedCount + 1;
                }
            }

            lastUserId = userIds[userIds.Count - 1];
        }

        logger.LogInformation("{NombreDUtilisateurs} calcul(s) de suggestions fait(s) en arrière-plan.", calculatedCount);
    }

    // Calcule le mois de cet utilisateur. Une panne va au journal technique, et le passage continue avec le suivant.
    private async Task<bool> TryCalculateAsync(Guid userId, DateOnly month, CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunInScopeAsync<SuggestionCalculationService, bool>(service => service.CalculateAsync(userId, month, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "Le calcul des suggestions de l'utilisateur {IdentifiantUtilisateur} a échoué.", userId);
            return false;
        }
    }
}
