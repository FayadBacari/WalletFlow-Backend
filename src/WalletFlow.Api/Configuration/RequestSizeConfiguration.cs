namespace WalletFlow.Api.Configuration;

// Taille maximale des requêtes de l'API.
public static class RequestSizeConfiguration
{
    // La plus longue adresse et le plus long mot de passe permis tiennent dans cette taille,
    // même si chaque caractère est écrit en \uXXXX dans le JSON.
    public const long MaximumAccountRequestBytes = 4096;

    // Le plus long nom de catégorie permis tient dans cette taille, même si chacun de ses 50 caractères
    // est un emoji écrit en \uXXXX\uXXXX dans le JSON (12 octets chacun, 600 en tout).
    public const long MaximumCategoryRequestBytes = 1024;

    // Le code et le state renvoyés par la banque, et le nom d'une banque, tiennent largement dans cette taille.
    public const long MaximumBankRequestBytes = 4096;

    // Un identifiant de catégorie et deux valeurs vrai ou faux tiennent largement dans cette taille.
    public const long MaximumTransactionRequestBytes = 1024;
}
