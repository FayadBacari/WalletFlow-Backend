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
}
