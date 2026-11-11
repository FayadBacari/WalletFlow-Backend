using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit le canal des alertes en code de la colonne canal_notification, et inversement.
public static class NotificationChannelDatabaseCodes
{
    // Code de la colonne canal_notification pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<NotificationChannel> Table =
        new EnumDatabaseCodes<NotificationChannel>(
            new Dictionary<NotificationChannel, string>
            {
                { NotificationChannel.Email, "email" },
                { NotificationChannel.Phone, "telephone" },
                { NotificationChannel.EmailAndPhone, "les_deux" },
            });
}
