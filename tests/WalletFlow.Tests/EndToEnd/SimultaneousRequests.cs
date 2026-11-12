using Microsoft.Extensions.DependencyInjection;

namespace WalletFlow.Tests.EndToEnd;

// Envoie des requêtes simultanées à la vraie API, pour les courses entre requêtes.
public static class SimultaneousRequests
{
    // Lance toutes les requêtes au même signal, chacune dans sa propre portée : son propre accès à la base, comme une requête de l'API.
    // Les résultats sont rendus dans l'ordre des numéros de requête.
    public static async Task<List<TResult>> RunAsync<TResult>(
        ApiFactoryWithRealDatabase api,
        int requestCount,
        Func<IServiceProvider, int, Task<TResult>> sendRequest)
    {
        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<Task<TResult>>();

        for (var requestNumber = 0; requestNumber < requestCount; requestNumber++)
        {
            var currentRequestNumber = requestNumber;
            requests.Add(Task.Run(async () =>
            {
                await startSignal.Task;
                await using var scope = api.Services.CreateAsyncScope();
                return await sendRequest(scope.ServiceProvider, currentRequestNumber);
            }));
        }

        startSignal.SetResult();
        var results = await Task.WhenAll(requests);

        return new List<TResult>(results);
    }
}
