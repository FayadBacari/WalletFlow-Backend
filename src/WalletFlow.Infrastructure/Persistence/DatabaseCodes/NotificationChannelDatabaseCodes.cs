using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit le canal des alertes en code de la colonne canal_notification, et inversement.
public static class NotificationChannelDatabaseCodes
{
    // Convertisseur utilisé par EF Core pour lire et écrire la colonne canal_notification.
    public static readonly ValueConverter<NotificationChannel, string> ConverterBetweenNotificationChannelAndDatabaseCode =
        new ValueConverter<NotificationChannel, string>(
            notificationChannelToWriteIntoTheDatabase => ConvertNotificationChannelIntoItsDatabaseCode(notificationChannelToWriteIntoTheDatabase),
            databaseCodeReadForTheNotificationChannel => ConvertDatabaseCodeIntoItsNotificationChannel(databaseCodeReadForTheNotificationChannel));

    // Donne le code enregistré en base pour un canal.
    public static string ConvertNotificationChannelIntoItsDatabaseCode(NotificationChannel notificationChannelToConvertIntoDatabaseCode)
    {
        switch (notificationChannelToConvertIntoDatabaseCode)
        {
            case NotificationChannel.Email:
                return "email";
            case NotificationChannel.Phone:
                return "telephone";
            case NotificationChannel.EmailAndPhone:
                return "les_deux";
            default:
                throw new ArgumentOutOfRangeException(nameof(notificationChannelToConvertIntoDatabaseCode));
        }
    }

    // Donne le canal qui correspond à un code lu en base.
    public static NotificationChannel ConvertDatabaseCodeIntoItsNotificationChannel(string databaseCodeToConvertIntoNotificationChannel)
    {
        switch (databaseCodeToConvertIntoNotificationChannel)
        {
            case "email":
                return NotificationChannel.Email;
            case "telephone":
                return NotificationChannel.Phone;
            case "les_deux":
                return NotificationChannel.EmailAndPhone;
            default:
                throw new ArgumentOutOfRangeException(nameof(databaseCodeToConvertIntoNotificationChannel));
        }
    }
}
