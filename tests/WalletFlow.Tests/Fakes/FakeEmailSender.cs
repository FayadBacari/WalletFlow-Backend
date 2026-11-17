using WalletFlow.Application.Emails;

namespace WalletFlow.Tests.Fakes;

// Remplace l'envoi des e-mails dans les tests : garde les e-mails en mémoire.
public class FakeEmailSender : IEmailSender
{
    private readonly object sentEmailsLock = new object();

    public List<EmailMessage> SentEmails { get; } = new List<EmailMessage>();

    // Simule une panne de l'envoi des e-mails.
    public bool AlwaysFails { get; set; } = false;

    // Garde l'e-mail en mémoire, pour que le test puisse le relire. Une tâche de fond peut envoyer pendant que le test lit.
    public Task SendAsync(EmailMessage message)
    {
        if (AlwaysFails)
        {
            throw new EmailCouldNotBeSentException(new IOException("panne simulée de l'envoi"));
        }

        lock (sentEmailsLock)
        {
            SentEmails.Add(message);
        }

        return Task.CompletedTask;
    }

    // Donne le dernier e-mail parti vers cette adresse, même pendant qu'une tâche de fond en envoie.
    public EmailMessage FindLastSentEmailTo(string recipient)
    {
        lock (sentEmailsLock)
        {
            return SentEmails.Last(email => email.RecipientAddress == recipient);
        }
    }

    // Compte les e-mails partis vers cette adresse, même pendant qu'une tâche de fond en envoie.
    public int CountSentEmailsTo(string recipient)
    {
        lock (sentEmailsLock)
        {
            return SentEmails.Count(email => email.RecipientAddress == recipient);
        }
    }

    // Donne les e-mails de cet objet partis vers cette adresse, même pendant qu'une tâche de fond en envoie.
    public List<EmailMessage> FindSentEmailsTo(string recipient, string subject)
    {
        lock (sentEmailsLock)
        {
            return SentEmails.Where(email => email.RecipientAddress == recipient && email.Subject == subject).ToList();
        }
    }
}
