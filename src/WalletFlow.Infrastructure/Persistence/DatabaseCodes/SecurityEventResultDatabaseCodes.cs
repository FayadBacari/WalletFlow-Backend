using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit le résultat d'un événement du journal en code de la colonne resultat_evenement, et inversement.
public static class SecurityEventResultDatabaseCodes
{
    // Convertisseur utilisé par EF Core pour lire et écrire la colonne resultat_evenement.
    public static readonly ValueConverter<SecurityEventResult, string> ConverterBetweenSecurityEventResultAndDatabaseCode =
        new ValueConverter<SecurityEventResult, string>(
            securityEventResultToWriteIntoTheDatabase => ConvertSecurityEventResultIntoItsDatabaseCode(securityEventResultToWriteIntoTheDatabase),
            databaseCodeReadForTheSecurityEventResult => ConvertDatabaseCodeIntoItsSecurityEventResult(databaseCodeReadForTheSecurityEventResult));

    // Donne le code enregistré en base pour un résultat.
    public static string ConvertSecurityEventResultIntoItsDatabaseCode(SecurityEventResult securityEventResultToConvertIntoDatabaseCode)
    {
        switch (securityEventResultToConvertIntoDatabaseCode)
        {
            case SecurityEventResult.Success:
                return "succes";
            case SecurityEventResult.Failure:
                return "echec";
            default:
                throw new ArgumentOutOfRangeException(nameof(securityEventResultToConvertIntoDatabaseCode));
        }
    }

    // Donne le résultat qui correspond à un code lu en base.
    public static SecurityEventResult ConvertDatabaseCodeIntoItsSecurityEventResult(string databaseCodeToConvertIntoSecurityEventResult)
    {
        switch (databaseCodeToConvertIntoSecurityEventResult)
        {
            case "succes":
                return SecurityEventResult.Success;
            case "echec":
                return SecurityEventResult.Failure;
            default:
                throw new ArgumentOutOfRangeException(nameof(databaseCodeToConvertIntoSecurityEventResult));
        }
    }
}
