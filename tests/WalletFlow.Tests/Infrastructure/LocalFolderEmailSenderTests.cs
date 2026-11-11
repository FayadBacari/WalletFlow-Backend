using WalletFlow.Application.Emails;
using WalletFlow.Infrastructure.Emails;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie l'écriture des e-mails dans un dossier local, utilisée en développement.
public class LocalFolderEmailSenderTests
{
    [Fact]
    public async Task EmailIsWrittenInNewFileWithItsRecipientSubjectAndBody()
    {
        var temporaryFolder = Path.Combine(Path.GetTempPath(), "walletflow-test-" + Guid.NewGuid());
        var sender = new LocalFolderEmailSender(temporaryFolder);

        try
        {
            await sender.SendAsync(
                new EmailMessage("destinataire@exemple.test", "Objet du test", "Texte du test"));

            var files = Directory.GetFiles(temporaryFolder);
            var file = Assert.Single(files);
            var content = await File.ReadAllTextAsync(file);

            Assert.Contains("destinataire@exemple.test", content);
            Assert.Contains("Objet du test", content);
            Assert.Contains("Texte du test", content);
        }
        finally
        {
            Directory.Delete(temporaryFolder, true);
        }
    }

    // Le dossier ne peut pas être créé, car un fichier porte déjà ce nom : l'erreur est celle que le service attend.
    [Fact]
    public async Task FolderThatCannotBeCreatedRaisesTheEmailFailure()
    {
        var blockingFilePath = Path.Combine(Path.GetTempPath(), "walletflow-test-" + Guid.NewGuid());
        await File.WriteAllTextAsync(blockingFilePath, "ce fichier prend la place du dossier");
        var sender = new LocalFolderEmailSender(blockingFilePath);

        try
        {
            var exception = await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() =>
                sender.SendAsync(
                    new EmailMessage("destinataire@exemple.test", "Objet du test", "Texte du test")));

            // Ce message peut finir dans les journaux : il ne contient jamais l'adresse (SS §7).
            Assert.DoesNotContain("destinataire@exemple.test", exception.Message);
        }
        finally
        {
            File.Delete(blockingFilePath);
        }
    }

    // Le dossier existe mais n'accepte aucune écriture : l'erreur est celle que le service attend.
    [Fact]
    public async Task FolderWithoutWritePermissionRaisesTheEmailFailure()
    {
        // Les droits d'un dossier se règlent autrement sous Windows : ce test ne vaut que pour macOS et Linux.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var temporaryFolder = Path.Combine(Path.GetTempPath(), "walletflow-test-" + Guid.NewGuid());
        Directory.CreateDirectory(temporaryFolder);
        File.SetUnixFileMode(temporaryFolder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var sender = new LocalFolderEmailSender(temporaryFolder);

        try
        {
            await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() =>
                sender.SendAsync(
                    new EmailMessage("destinataire@exemple.test", "Objet du test", "Texte du test")));
        }
        finally
        {
            File.SetUnixFileMode(temporaryFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(temporaryFolder, true);
        }
    }
}
