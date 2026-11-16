using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Notifications;

// Les réglages d'alerte d'un utilisateur, tels qu'il les voit (GR Notif) : le canal choisi, et chacun des 10 types d'alerte,
// dans l'ordre de la liste. Tant que l'application mobile n'existe pas, toutes les alertes partent par e-mail, quel que soit le canal.
public record AlertSettings(NotificationChannel Channel, List<AlertSetting> Alerts);

// Un type d'alerte et le choix de l'utilisateur. Une alerte de sécurité ne se désactive pas : elle est toujours activée.
public record AlertSetting(AlertKind Kind, string Label, bool CanBeDisabled, bool IsEnabled);
