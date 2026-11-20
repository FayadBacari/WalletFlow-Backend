namespace WalletFlow.Tests.Api;

// Un client qui parle au vrai serveur Kestrel des tests comme le proxy inverse de la même machine en production : en HTTP, avec
// X-Forwarded-Proto: https. Kestrel ne sert que du HTTP, et les routes de l'API refusent une requête en HTTP simple (voir
// HttpsOnlyMiddleware).
public static class LocalProxyClient
{
    // Démarre la vraie API, avant de créer le client : l'adresse de Kestrel, avec son port, n'est connue qu'une fois le serveur
    // démarré. Créé avant, le client garderait l'adresse HTTPS des clients de test, où rien n'écoute.
    public static HttpClient Create(ApiFactoryWithFakes api)
    {
        api.StartServer();
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        return client;
    }
}
