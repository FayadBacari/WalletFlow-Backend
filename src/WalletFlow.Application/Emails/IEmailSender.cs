namespace WalletFlow.Application.Emails;

// Envoi des e-mails. Implémenté dans la couche Infrastructure.
public interface IEmailSender
{
    // Envoie un e-mail à son destinataire. Lève EmailCouldNotBeSentException s'il ne peut pas partir.
    Task SendAsync(EmailMessage message);
}
