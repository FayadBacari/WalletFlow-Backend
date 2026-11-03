using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit l'état d'une demande de support en code de la colonne statut_demande, et inversement.
public static class SupportRequestStatusDatabaseCodes
{
    // Convertisseur utilisé par EF Core pour lire et écrire la colonne statut_demande.
    public static readonly ValueConverter<SupportRequestStatus, string> ConverterBetweenSupportRequestStatusAndDatabaseCode =
        new ValueConverter<SupportRequestStatus, string>(
            supportRequestStatusToWriteIntoTheDatabase => ConvertSupportRequestStatusIntoItsDatabaseCode(supportRequestStatusToWriteIntoTheDatabase),
            databaseCodeReadForTheSupportRequestStatus => ConvertDatabaseCodeIntoItsSupportRequestStatus(databaseCodeReadForTheSupportRequestStatus));

    // Donne le code enregistré en base pour un état de demande.
    public static string ConvertSupportRequestStatusIntoItsDatabaseCode(SupportRequestStatus supportRequestStatusToConvertIntoDatabaseCode)
    {
        switch (supportRequestStatusToConvertIntoDatabaseCode)
        {
            case SupportRequestStatus.Sent:
                return "envoyee";
            case SupportRequestStatus.InProgress:
                return "en_cours";
            case SupportRequestStatus.Resolved:
                return "resolue";
            default:
                throw new ArgumentOutOfRangeException(nameof(supportRequestStatusToConvertIntoDatabaseCode));
        }
    }

    // Donne l'état de demande qui correspond à un code lu en base.
    public static SupportRequestStatus ConvertDatabaseCodeIntoItsSupportRequestStatus(string databaseCodeToConvertIntoSupportRequestStatus)
    {
        switch (databaseCodeToConvertIntoSupportRequestStatus)
        {
            case "envoyee":
                return SupportRequestStatus.Sent;
            case "en_cours":
                return SupportRequestStatus.InProgress;
            case "resolue":
                return SupportRequestStatus.Resolved;
            default:
                throw new ArgumentOutOfRangeException(nameof(databaseCodeToConvertIntoSupportRequestStatus));
        }
    }
}
