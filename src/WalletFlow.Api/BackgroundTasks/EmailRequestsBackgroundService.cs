using WalletFlow.Application.Emails;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : envoie les e-mails de la file, un par un, dans l'ordre de leur heure d'essai (SS §9).
// Elle se réveille dès qu'une demande entre dans la file, et passe aussi toutes les 30 secondes, pour les essais prévus
// plus tard. Comme le reste de l'état gardé en mémoire, elle suppose un seul serveur.
public class EmailRequestsBackgroundService : RecurringBackgroundService
{
    // Le passage de secours, quand rien ne réveille la tâche : il trouve les essais prévus plus tard.
    public static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(30);

    // Au plus ce nombre de demandes par passage : la tâche s'arrête de temps en temps, quoi qu'il arrive.
    public const int MaximumRequestsPerPass = 1000;

    private readonly EmailRequestWakeUp wakeUp;
    private readonly ILogger<EmailRequestsBackgroundService> logger;

    // Reçoit le réveil, de quoi créer un accès à la base pour chaque essai, l'horloge, le bilan de santé des tâches et le journal
    // technique. Le réveil remplace l'attente sur l'horloge : voir WaitForNextPassAsync.
    public EmailRequestsBackgroundService(
        EmailRequestWakeUp wakeUp,
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        BackgroundTaskHealth health,
        ILogger<EmailRequestsBackgroundService> logger)
        : base(scopeFactory, clock, MaximumWait, health)
    {
        this.wakeUp = wakeUp;
        this.logger = logger;
    }

    // Attend qu'une demande entre dans la file, ou au plus le passage de secours.
    protected override Task WaitForNextPassAsync(CancellationToken stoppingToken)
    {
        return wakeUp.WaitAsync(MaximumWait, stoppingToken);
    }

    // Envoie les demandes dont l'heure est venue. Une panne n'arrête pas la tâche : elle va au journal technique,
    // et le passage reprend au prochain réveil. Le journal désigne l'utilisateur par son identifiant, jamais par son adresse (SS §7).
    public override async Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        for (var requestNumber = 1; requestNumber <= MaximumRequestsPerPass; requestNumber++)
        {
            var requestId = await TryFindNextDueRequestAsync(cancellationToken);
            if (requestId == null)
            {
                // Une file vide au premier regard : ce passage ne dit rien de la santé de l'envoi.
                if (requestNumber == 1)
                {
                    ReportIdlePass();
                }

                return;
            }

            if (!await TryProcessAsync(requestId.Value, cancellationToken))
            {
                return;
            }
        }
    }

    // Cherche la prochaine demande. Si la base ne répond pas, le passage s'arrête là.
    private async Task<Guid?> TryFindNextDueRequestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunInScopeAsync<EmailRequestProcessingService, Guid?>(processingService => processingService.FindNextDueRequestIdAsync(cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "La file des e-mails ne peut pas être lue.");
            return null;
        }
    }

    // Fait un essai pour la demande. Une panne imprévue compte comme un essai raté : sinon, la même demande reviendrait
    // en tête de la file à chaque passage. Renvoie faux si même cet essai raté ne peut pas être noté.
    private async Task<bool> TryProcessAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        try
        {
            WriteOutcome(await RunInScopeAsync<EmailRequestProcessingService, EmailRequestOutcome>(processingService => processingService.ProcessAsync(requestId, cancellationToken), cancellationToken));
            return true;
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "L'essai d'une demande d'e-mail a échoué.");
        }

        try
        {
            WriteOutcome(await RunInScopeAsync<EmailRequestProcessingService, EmailRequestOutcome>(processingService => processingService.RecordUnexpectedFailureAsync(requestId, cancellationToken), cancellationToken));
            return true;
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "L'essai raté d'une demande d'e-mail n'a pas pu être noté.");
            return false;
        }
    }

    // Écrit au journal technique un e-mail qui n'est pas parti : son passage échoue aussi dans le bilan de santé, car le serveur
    // d'e-mails peut être en panne.
    private void WriteOutcome(EmailRequestOutcome outcome)
    {
        if (outcome is EmailRequestOutcome.WillRetry retry)
        {
            ReportFailure();
            logger.LogWarning(
                "Un e-mail de type {TypeDeDemande} n'a pas pu partir pour l'utilisateur {IdentifiantUtilisateur} : essai {NumeroEssai} raté, le suivant est prévu à {DateProchainEssai}.",
                retry.Type,
                retry.UserId,
                retry.FailedTryCount,
                retry.NextTryAt);
        }

        if (outcome is EmailRequestOutcome.GivenUp givenUp)
        {
            ReportFailure();
            logger.LogError(
                "Un e-mail de type {TypeDeDemande} n'a pas pu partir pour l'utilisateur {IdentifiantUtilisateur} : abandonné après le dernier essai.",
                givenUp.Type,
                givenUp.UserId);
        }
    }
}
