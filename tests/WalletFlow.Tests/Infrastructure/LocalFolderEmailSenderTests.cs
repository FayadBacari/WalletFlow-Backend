using WalletFlow.Application.Emails;
using WalletFlow.Infrastructure.Emails;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie l'écriture des e-mails dans un dossier local, utilisée en développement.
public class LocalFolderEmailSenderTests
{
    [Fact]
    public async Task EmailIsWrittenInNewFileWithItsRecipientSubjectAndBody()
    {
        var temporaryFolderCreatedForThisTest = Path.Combine(Path.GetTempPath(), "walletflow-test-" + Guid.NewGuid());
        var senderBeingTestedWithTemporaryFolder = new LocalFolderEmailSender(temporaryFolderCreatedForThisTest);

        try
        {
            await senderBeingTestedWithTemporaryFolder.SendEmailToItsRecipientAsync(
                new EmailMessageToSend("destinataire@exemple.test", "Objet du test", "Texte du test"));

            var filesWrittenInTheTemporaryFolder = Directory.GetFiles(temporaryFolderCreatedForThisTest);
            var fileWrittenForTheEmailBeingTested = Assert.Single(filesWrittenInTheTemporaryFolder);
            var contentOfTheFileWrittenByTheSender = await File.ReadAllTextAsync(fileWrittenForTheEmailBeingTested);

            Assert.Contains("destinataire@exemple.test", contentOfTheFileWrittenByTheSender);
            Assert.Contains("Objet du test", contentOfTheFileWrittenByTheSender);
            Assert.Contains("Texte du test", contentOfTheFileWrittenByTheSender);
        }
        finally
        {
            Directory.Delete(temporaryFolderCreatedForThisTest, true);
        }
    }

    // Le dossier ne peut pas être créé, car un fichier porte déjà ce nom : l'erreur est celle que le service attend.
    [Fact]
    public async Task FolderThatCannotBeCreatedRaisesTheEmailFailure()
    {
        var pathOfTheFileBlockingTheFolder = Path.Combine(Path.GetTempPath(), "walletflow-test-" + Guid.NewGuid());
        await File.WriteAllTextAsync(pathOfTheFileBlockingTheFolder, "ce fichier prend la place du dossier");
        var senderBeingTestedWithBlockedFolder = new LocalFolderEmailSender(pathOfTheFileBlockingTheFolder);

        try
        {
            var failureRaisedByTheEmailSender = await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() =>
                senderBeingTestedWithBlockedFolder.SendEmailToItsRecipientAsync(
                    new EmailMessageToSend("destinataire@exemple.test", "Objet du test", "Texte du test")));

            // Ce message peut finir dans les journaux : il ne contient jamais l'adresse (SS §7).
            Assert.DoesNotContain("destinataire@exemple.test", failureRaisedByTheEmailSender.Message);
        }
        finally
        {
            File.Delete(pathOfTheFileBlockingTheFolder);
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

        var temporaryFolderCreatedForThisTest = Path.Combine(Path.GetTempPath(), "walletflow-test-" + Guid.NewGuid());
        Directory.CreateDirectory(temporaryFolderCreatedForThisTest);
        File.SetUnixFileMode(temporaryFolderCreatedForThisTest, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var senderBeingTestedWithReadOnlyFolder = new LocalFolderEmailSender(temporaryFolderCreatedForThisTest);

        try
        {
            await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() =>
                senderBeingTestedWithReadOnlyFolder.SendEmailToItsRecipientAsync(
                    new EmailMessageToSend("destinataire@exemple.test", "Objet du test", "Texte du test")));
        }
        finally
        {
            File.SetUnixFileMode(temporaryFolderCreatedForThisTest, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(temporaryFolderCreatedForThisTest, true);
        }
    }
}
