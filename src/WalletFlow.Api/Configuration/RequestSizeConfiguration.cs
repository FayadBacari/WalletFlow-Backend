namespace WalletFlow.Api.Configuration;

// Taille maximale des requêtes de l'API.
public static class RequestSizeConfiguration
{
    // La plus longue adresse et le plus long mot de passe permis tiennent dans cette taille,
    // même si chaque caractère est écrit en \uXXXX dans le JSON.
    public const long MaximumNumberOfBytesInOneAccountRequest = 4096;
}
