using WalletFlow.Application.Emails;

namespace WalletFlow.Application.Notifications;

// Les e-mails des quatre alertes qu'on peut désactiver (GR Notif). Implémenté dans la couche Infrastructure, qui en écrit
// le texte. Aucun ne contient de montant ni de lien : l'utilisateur ouvre lui-même WalletFlow (SS §9).
public interface IAlertEmails
{
    // Crée l'e-mail de cette alerte pour ce destinataire.
    EmailMessage CreateEmail(string recipient, AlertEmailContent content);
}
