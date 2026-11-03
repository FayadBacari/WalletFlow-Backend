namespace WalletFlow.Domain.UsersAndSecurity;

// Canal de réception des alertes (canal_notification, DD §3).
public enum NotificationChannel
{
    Email,        // email
    Phone,        // telephone
    EmailAndPhone // les_deux
}
