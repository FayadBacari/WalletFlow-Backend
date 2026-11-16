using WalletFlow.Infrastructure.Emails;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le texte en français de la réponse du support (GR Admin, SS §9) : la date de la demande en toutes lettres, le texte de
// la réponse tel quel, en texte brut, puis la page de support, seule sur la dernière ligne.
public class FrenchSupportEmailsTests
{
    private const string SupportPage = "https://walletflow.test/support";

    private readonly FrenchSupportEmails emails = new FrenchSupportEmails();

    [Theory]
    [InlineData(2026, 11, 1, "ta demande du 1er novembre 2026")]
    [InlineData(2027, 8, 22, "ta demande du 22 août 2027")]
    public void ResponseGivesTheDayOfTheRequest(int year, int month, int day, string expectedWords)
    {
        var email = emails.CreateResponseEmail("utilisateur@exemple.test", new DateTime(year, month, day, 23, 30, 0), "Réponse", SupportPage);

        Assert.Contains("Le support de WalletFlow a répondu à " + expectedWords + " :", email.Body);
    }

    // Le texte part tel quel, retours à la ligne compris : un texte qui ressemble à du HTML reste du texte (SS §9).
    [Fact]
    public void ResponseTextIsKeptAsPlainText()
    {
        var text = "Bonjour,\n<b>Voici</b> la marche à suivre : https://walletflow.test/aide & bon courage.";

        var email = emails.CreateResponseEmail("visiteur@exemple.test", new DateTime(2026, 11, 16, 9, 0, 0), text, SupportPage);

        Assert.Equal("visiteur@exemple.test", email.RecipientAddress);
        Assert.Equal("Réponse du support de WalletFlow", email.Subject);
        Assert.Contains("\n\n" + text + "\n\n", email.Body);
        Assert.EndsWith("écris une nouvelle demande au support de WalletFlow :\n" + SupportPage, email.Body);
        Assert.StartsWith("Bonjour,", email.Body);
    }
}
