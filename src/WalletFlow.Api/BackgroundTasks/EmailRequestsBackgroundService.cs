using WalletFlow.Application.Emails;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : envoie les e-mails de la file, un par un, dans l'ordre de leur heure d'essai (SS §9).
// Elle se réveille dès qu'une demande entre dans la file, et passe aussi toutes les 30 secondes, pour les essais prévus
// plus tard. Comme le reste de l'état gardé en mémoire, elle suppose un seul serveur.
public class EmailRequestsBackgroundService : BackgroundService
{
    // Le passage de secours, quand rien ne réveille la tâche : il trouve les essais prévus plus tard.
    public static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(30);

    // Au plus ce nombre de demandes par passage : la tâche s'arrête de temps en temps, quoi qu'il arrive.
    public const int MaximumRequestsPerPass = 1000;

    private readonly EmailRequestWakeUp wakeUp;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<EmailRequestsBackgroundService> logger;

    // Reçoit le réveil, de quoi créer un accès à la base pour chaque essai, et le journal technique.
    public EmailRequestsBackgroundService(
        EmailRequestWakeUp wakeUp,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailRequestsBackgroundService> logger)
    {
        this.wakeUp = wakeUp;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    // Tourne tant que l'API tourne (nom imposé par le framework).
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessDueRequestsAsync();

            try
            {
                await wakeUp.WaitAsync(MaximumWait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Envoie les demandes dont l'heure est venue. Une panne n'arrête pas la tâche : elle va au journal technique,
    // et le passage reprend au prochain réveil. Le journal désigne l'utilisateur par son identifiant, jamais par son adresse (SS §7).
    public async Task ProcessDueRequestsAsync()
    {
        for (var requestNumber = 1; requestNumber <= MaximumRequestsPerPass; requestNumber++)
        {
            var requestId = await TryFindNextDueRequestAsync();
            if (requestId == null)
            {
                return;
            }

            if (!await TryProcessAsync(requestId.Value))
            {
                return;
            }
        }
    }

    // Cherche la prochaine demande. Si la base ne répond pas, le passage s'arrête là.
    private async Task<Guid?> TryFindNextDueRequestAsync()
    {
        try
        {
            return await RunInScopeAsync(processingService => processingService.FindNextDueRequestIdAsync());
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "La file des e-mails ne peut pas être lue.");
            return null;
        }
    }

    // Fait un essai pour la demande. Une panne imprévue compte comme un essai raté : sinon, la même demande reviendrait
    // en tête de la file à chaque passage. Renvoie faux si même cet essai raté ne peut pas être noté.
    private async Task<bool> TryProcessAsync(Guid requestId)
    {
        try
        {
            WriteOutcome(await RunInScopeAsync(processingService => processingService.ProcessAsync(requestId)));
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "L'essai d'une demande d'e-mail a échoué.");
        }

        try
        {
            WriteOutcome(await RunInScopeAsync(processingService => processingService.RecordUnexpectedFailureAsync(requestId)));
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "L'essai raté d'une demande d'e-mail n'a pas pu être noté.");
            return false;
        }
    }

    // Écrit au journal technique un e-mail qui n'est pas parti.
    private void WriteOutcome(EmailRequestOutcome outcome)
    {
        if (outcome is EmailRequestOutcome.WillRetry retry)
        {
            logger.LogWarning(
                "Un e-mail de type {TypeDeDemande} n'a pas pu partir pour l'utilisateur {IdentifiantUtilisateur} : essai {NumeroEssai} raté, le suivant est prévu à {DateProchainEssai}.",
                retry.Type,
                retry.UserId,
                retry.FailedTryCount,
                retry.NextTryAt);
        }

        if (outcome is EmailRequestOutcome.GivenUp givenUp)
        {
            logger.LogError(
                "Un e-mail de type {TypeDeDemande} n'a pas pu partir pour l'utilisateur {IdentifiantUtilisateur} : abandonné après le dernier essai.",
                givenUp.Type,
                givenUp.UserId);
        }
    }

    // Donne à chaque étape son propre accès à la base, oublié ensuite.
    private async Task<TResult> RunInScopeAsync<TResult>(Func<EmailRequestProcessingService, Task<TResult>> step)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processingService = scope.ServiceProvider.GetRequiredService<EmailRequestProcessingService>();

        return await step(processingService);
    }
}
