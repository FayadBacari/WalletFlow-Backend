using WalletFlow.Application.AccountRecovery;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : prépare et envoie les e-mails de récupération et de déblocage, un par un, dans l'ordre de la file.
public class BackgroundEmailRequestsBackgroundService : BackgroundService
{
    private readonly IBackgroundEmailRequestQueue queueOfTheEmailsToPrepareInTheBackground;
    private readonly IServiceScopeFactory factoryOfTheScopesUsedByTheEmailRequests;
    private readonly ILogger<BackgroundEmailRequestsBackgroundService> loggerOfTheBackgroundEmailRequests;

    // Reçoit la file, de quoi créer un accès à la base pour chaque demande, et le journal technique.
    public BackgroundEmailRequestsBackgroundService(
        IBackgroundEmailRequestQueue queueOfTheEmailsToPrepareInTheBackground,
        IServiceScopeFactory factoryOfTheScopesUsedByTheEmailRequests,
        ILogger<BackgroundEmailRequestsBackgroundService> loggerOfTheBackgroundEmailRequests)
    {
        this.queueOfTheEmailsToPrepareInTheBackground = queueOfTheEmailsToPrepareInTheBackground;
        this.factoryOfTheScopesUsedByTheEmailRequests = factoryOfTheScopesUsedByTheEmailRequests;
        this.loggerOfTheBackgroundEmailRequests = loggerOfTheBackgroundEmailRequests;
    }

    // Tourne tant que l'API tourne (nom imposé par le framework).
    protected override async Task ExecuteAsync(CancellationToken tokenThatStopsTheEmailRequestsTask)
    {
        while (!tokenThatStopsTheEmailRequestsTask.IsCancellationRequested)
        {
            var nextEmailRequestOfTheQueue = await queueOfTheEmailsToPrepareInTheBackground.WaitForNextEmailRequestOfTheQueueAsync(tokenThatStopsTheEmailRequestsTask);

            await ProcessOneEmailRequestOfTheQueueAsync(nextEmailRequestOfTheQueue);
        }
    }

    // Traite une demande. Une panne ne doit pas arrêter la tâche : les demandes suivantes doivent encore partir.
    // Le journal désigne l'utilisateur par son identifiant, jamais par son adresse (SS §7).
    public async Task ProcessOneEmailRequestOfTheQueueAsync(BackgroundEmailRequest nextEmailRequestOfTheQueue)
    {
        try
        {
            using var scopeOfThisBackgroundEmailRequest = factoryOfTheScopesUsedByTheEmailRequests.CreateScope();
            var serviceThatProcessesTheEmailRequests = scopeOfThisBackgroundEmailRequest.ServiceProvider.GetRequiredService<BackgroundEmailRequestProcessingService>();

            var resultOfTheBackgroundEmailRequest = await serviceThatProcessesTheEmailRequests.ProcessOneBackgroundEmailRequestAsync(nextEmailRequestOfTheQueue);

            if (resultOfTheBackgroundEmailRequest.EmailCouldNotBeSent)
            {
                loggerOfTheBackgroundEmailRequests.LogError(
                    "Un e-mail de type {TypeDeDemande} n'a pas pu partir pour l'utilisateur {IdentifiantUtilisateur}.",
                    nextEmailRequestOfTheQueue.TypeOfTheRequestedBackgroundEmail,
                    resultOfTheBackgroundEmailRequest.UserIdentifierForTheTechnicalLog);
            }
        }
        catch (Exception technicalErrorRaisedByTheEmailRequest)
        {
            loggerOfTheBackgroundEmailRequests.LogError(technicalErrorRaisedByTheEmailRequest, "Le traitement d'une demande d'e-mail a échoué.");
        }
    }
}
