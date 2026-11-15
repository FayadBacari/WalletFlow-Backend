using WalletFlow.Application.Emails;

namespace WalletFlow.Application.Notifications;

// E-mails des alertes de sécurité qu'on ne peut pas désactiver (GR Notif). L'Application choisit l'e-mail et ses données ;
// l'Infrastructure en écrit le texte. Ils ne citent aucun montant, et disent quoi faire si ce n'est pas l'utilisateur
// qui a agi (SS §9).
public interface ISecurityAlertEmails
{
    // Crée l'alerte d'une connexion depuis un appareil que le compte ne connaissait pas, à cette heure de Paris.
    EmailMessage CreateNewDeviceEmail(string recipient, DateTime loginTimeInParis);

    // Crée l'alerte d'un changement de mot de passe, à cette heure de Paris.
    EmailMessage CreatePasswordChangedEmail(string recipient, DateTime changeTimeInParis);
}
