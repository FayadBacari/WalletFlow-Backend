using WalletFlow.Application.AccountRecovery;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : prépare et envoie les e-mails de récupération et de déblocage, un par un, dans l'ordre de la file.
public class BackgroundEmailRequestsBackgroundService : BackgroundService
{
    private readonly IBackgroundEmailRequestQueue emailQueue;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<BackgroundEmailRequestsBackgroundService> logger;

    // Reçoit la file, de quoi créer un accès à la base pour chaque demande, et le journal technique.
    public BackgroundEmailRequestsBackgroundService(
        IBackgroundEmailRequestQueue emailQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<BackgroundEmailRequestsBackgroundService> logger)
    {
        this.emailQueue = emailQueue;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    // Tourne tant que l'API tourne (nom imposé par le framework).
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var request = await emailQueue.WaitForNextAsync(stoppingToken);

            await ProcessNextRequestAsync(request);
        }
    }

    // Traite une demande. Une panne ne doit pas arrêter la tâche : les demandes suivantes doivent encore partir.
    // Le journal désigne l'utilisateur par son identifiant, jamais par son adresse (SS §7).
    public async Task ProcessNextRequestAsync(BackgroundEmailRequest request)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var processingService = scope.ServiceProvider.GetRequiredService<BackgroundEmailRequestProcessingService>();

            var result = await processingService.ProcessAsync(request);

            if (result.EmailCouldNotBeSent)
            {
                logger.LogError(
                    "Un e-mail de type {TypeDeDemande} n'a pas pu partir pour l'utilisateur {IdentifiantUtilisateur}.",
                    request.Type,
                    result.UserId);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Le traitement d'une demande d'e-mail a échoué.");
        }
    }
}
