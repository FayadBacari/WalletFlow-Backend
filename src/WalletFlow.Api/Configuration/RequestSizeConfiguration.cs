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

    // Le plus long nom de sac permis, écrit comme celui d'une catégorie (600 octets au plus), avec un objectif et sa date,
    // tient dans cette taille. Deux identifiants de sac et un montant aussi.
    public const long MaximumBagRequestBytes = 1024;

    // Un identifiant de sac et un montant tiennent largement dans cette taille.
    public const long MaximumSuggestionRequestBytes = 1024;

    // Le nom d'un canal ou une valeur vrai ou faux tiennent largement dans cette taille.
    public const long MaximumAlertSettingsRequestBytes = 1024;

    // Le plus long message permis tient dans cette taille, même si chacun de ses 5 000 caractères est un emoji écrit
    // en \uXXXX\uXXXX dans le JSON (12 octets chacun, 60 000 en tout), avec l'adresse de contact d'un visiteur.
    public const long MaximumSupportRequestBytes = 65536;

    // Les actions de l'administrateur sur un compte ou une demande n'ont pas de corps : cette taille suffit largement.
    public const long MaximumAdministrationRequestBytes = 1024;
}
