namespace WalletFlow.Application.Emails;

// Envoi des e-mails. Implémenté dans la couche Infrastructure.
public interface IEmailSender
{
    // Envoie un e-mail à son destinataire.
    Task SendEmailToItsRecipientAsync(EmailMessageToSend emailMessageToSendToItsRecipient);
}
