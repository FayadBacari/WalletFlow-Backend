using WalletFlow.Application.Emails;

namespace WalletFlow.Infrastructure.Emails;

// Hors du développement, tant que l'envoi réel des e-mails n'est pas branché : l'API refuse de démarrer avec lui (voir
// Program.cs). S'il servait quand même, aucun e-mail ne partirait, et aucun ne serait écrit sur le disque (SS §9).
public sealed class EmailSenderNotConfigured : IEmailSender
{
    // Refuse l'envoi : il n'y a aucun moyen d'envoyer.
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        throw new EmailCouldNotBeSentException(new InvalidOperationException("Aucun envoi réel des e-mails n'est branché."));
    }
}
