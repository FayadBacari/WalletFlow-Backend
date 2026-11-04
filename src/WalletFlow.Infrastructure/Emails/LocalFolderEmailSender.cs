using WalletFlow.Application.Emails;

namespace WalletFlow.Infrastructure.Emails;

// En développement : écrit chaque e-mail dans un fichier d'un dossier local, au lieu de l'envoyer.
// Ce dossier est ignoré par Git, car les e-mails contiennent des liens à usage unique.
public class LocalFolderEmailSender : IEmailSender
{
    private readonly string pathOfTheFolderThatReceivesTheEmails;

    // Reçoit le chemin du dossier où écrire les e-mails.
    public LocalFolderEmailSender(string pathOfTheFolderThatReceivesTheEmails)
    {
        this.pathOfTheFolderThatReceivesTheEmails = pathOfTheFolderThatReceivesTheEmails;
    }

    // Écrit l'e-mail dans un nouveau fichier, dont le nom commence par sa date.
    public async Task SendEmailToItsRecipientAsync(EmailMessageToSend emailMessageToSendToItsRecipient)
    {
        Directory.CreateDirectory(pathOfTheFolderThatReceivesTheEmails);

        var nameOfTheFileCreatedForThisEmail = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid() + ".txt";
        var pathOfTheFileCreatedForThisEmail = Path.Combine(pathOfTheFolderThatReceivesTheEmails, nameOfTheFileCreatedForThisEmail);

        var contentOfTheFileCreatedForThisEmail =
            "À : " + emailMessageToSendToItsRecipient.RecipientAddress + "\n" +
            "Objet : " + emailMessageToSendToItsRecipient.Subject + "\n\n" +
            emailMessageToSendToItsRecipient.Body + "\n";

        await File.WriteAllTextAsync(pathOfTheFileCreatedForThisEmail, contentOfTheFileCreatedForThisEmail);
    }
}
