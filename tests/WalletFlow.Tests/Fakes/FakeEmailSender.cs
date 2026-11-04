using WalletFlow.Application.Emails;

namespace WalletFlow.Tests.Fakes;

// Remplace l'envoi des e-mails dans les tests : garde les e-mails en mémoire.
public class FakeEmailSender : IEmailSender
{
    public List<EmailMessageToSend> SentEmails { get; } = new List<EmailMessageToSend>();

    // Garde l'e-mail en mémoire, pour que le test puisse le relire.
    public Task SendEmailToItsRecipientAsync(EmailMessageToSend emailMessageToSendToItsRecipient)
    {
        SentEmails.Add(emailMessageToSendToItsRecipient);
        return Task.CompletedTask;
    }
}
