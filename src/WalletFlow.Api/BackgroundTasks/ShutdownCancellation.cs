namespace WalletFlow.Api.BackgroundTasks;

// Reconnaît l'arrêt de l'API dans une tâche de fond. L'annulation demandée par l'arrêt n'est pas une panne : elle remonte
// jusqu'au framework, qui l'attend, au lieu d'aller au journal technique comme une erreur, et la tâche ne continue pas sa
// liste avec un jeton déjà annulé. Un délai dépassé chez un prestataire reste une panne : son jeton à lui n'est pas annulé.
public static class ShutdownCancellation
{
    // Vrai si cette exception vient de l'arrêt de l'API.
    public static bool IsShutdown(Exception exception, CancellationToken stoppingToken)
    {
        return exception is OperationCanceledException && stoppingToken.IsCancellationRequested;
    }
}
