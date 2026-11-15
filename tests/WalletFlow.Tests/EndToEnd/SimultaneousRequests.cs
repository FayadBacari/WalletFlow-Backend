using Microsoft.Extensions.DependencyInjection;

namespace WalletFlow.Tests.EndToEnd;

// Envoie des requêtes simultanées à la vraie API, pour les courses entre requêtes.
public static class SimultaneousRequests
{
    // Lance toutes les requêtes au même signal, chacune dans sa propre portée : son propre accès à la base, comme une requête de l'API.
    // Les résultats sont rendus dans l'ordre des numéros de requête.
    // L'API démarre au premier accès à ses services, et ce démarrage n'est pas protégé contre des accès simultanés : lancé par
    // les requêtes elles-mêmes, il démarrait jusqu'à 20 API, chacune avec ses tâches de fond. Leurs 20 files d'e-mails envoyaient
    // alors deux fois le même e-mail, et leurs connexions remplissaient la base. L'API démarre donc ici, une fois, avant le signal.
    public static async Task<List<TResult>> RunAsync<TResult>(
        ApiFactoryWithRealDatabase api,
        int requestCount,
        Func<IServiceProvider, int, Task<TResult>> sendRequest)
    {
        var startedServices = api.Services;
        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<Task<TResult>>();

        for (var requestNumber = 0; requestNumber < requestCount; requestNumber++)
        {
            var currentRequestNumber = requestNumber;
            requests.Add(Task.Run(async () =>
            {
                await startSignal.Task;
                await using var scope = startedServices.CreateAsyncScope();
                return await sendRequest(scope.ServiceProvider, currentRequestNumber);
            }));
        }

        startSignal.SetResult();
        var results = await Task.WhenAll(requests);

        return new List<TResult>(results);
    }
}
