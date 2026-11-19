using System.Globalization;
using WalletFlow.Application.Emails;

namespace WalletFlow.Infrastructure.Emails;

// En développement : écrit chaque e-mail dans un fichier d'un dossier local, au lieu de l'envoyer.
// Ce dossier est ignoré par Git, car les e-mails contiennent des liens à usage unique.
public class LocalFolderEmailSender : IEmailSender
{
    private readonly string folderPath;
    private readonly TimeProvider clock;

    // Reçoit le chemin du dossier où écrire les e-mails, et l'horloge qui date chaque fichier.
    public LocalFolderEmailSender(string folderPath, TimeProvider clock)
    {
        this.folderPath = folderPath;
        this.clock = clock;
    }

    // Écrit l'e-mail dans un nouveau fichier, dont le nom commence par sa date.
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await WriteFileAsync(message, cancellationToken);
        }
        catch (IOException exception)
        {
            throw new EmailCouldNotBeSentException(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new EmailCouldNotBeSentException(exception);
        }
    }

    // Crée le dossier s'il n'existe pas, puis y écrit l'e-mail.
    private async Task WriteFileAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folderPath);

        var fileName = clock.GetUtcNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid() + ".txt";
        var filePath = Path.Combine(folderPath, fileName);

        var content =
            "À : " + message.RecipientAddress + "\n" +
            "Objet : " + message.Subject + "\n\n" +
            message.Body + "\n";

        await File.WriteAllTextAsync(filePath, content, cancellationToken);
    }
}
