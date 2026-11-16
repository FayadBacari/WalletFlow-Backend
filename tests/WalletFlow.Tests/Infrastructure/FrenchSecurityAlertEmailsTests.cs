using WalletFlow.Application.Emails;
using WalletFlow.Infrastructure.Emails;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le texte en français des alertes de sécurité (GR Notif, SS §9).
public class FrenchSecurityAlertEmailsTests
{
    private const string Recipient = "utilisateur@exemple.test";
    private const string SupportPage = "https://walletflow.test/support";

    private readonly FrenchSecurityAlertEmails emails = new FrenchSecurityAlertEmails();

    // Les quatre alertes de sécurité écrites par cette classe, au même moment.
    private List<EmailMessage> CreateAll(DateTime moment)
    {
        return new List<EmailMessage>
        {
            emails.CreateNewDeviceEmail(Recipient, moment, SupportPage),
            emails.CreatePasswordChangedEmail(Recipient, moment, SupportPage),
            emails.CreateTwoFactorDeactivatedEmail(Recipient, moment, SupportPage),
            emails.CreateBackupCodeUsedEmail(Recipient, moment, SupportPage)
        };
    }

    // Le premier du mois s'écrit « 1er », et les minutes ont toujours deux chiffres.
    [Theory]
    [InlineData(2026, 11, 1, 9, 5, "le 1er novembre 2026 à 9 h 05")]
    [InlineData(2026, 12, 31, 23, 59, "le 31 décembre 2026 à 23 h 59")]
    [InlineData(2027, 8, 2, 0, 0, "le 2 août 2027 à 0 h 00")]
    public void MomentIsWrittenInFrenchWords(int year, int month, int day, int hour, int minute, string text)
    {
        var moment = new DateTime(year, month, day, hour, minute, 0);

        Assert.All(CreateAll(moment), email => Assert.Contains(text + " (heure de Paris)", email.Body));
    }

    // Une alerte ne cite aucun montant (GR Notif). Son seul lien est la page de support, seul sur la dernière ligne :
    // une alerte ne sert jamais à se connecter (SS §9).
    [Fact]
    public void OnlyLinkIsTheSupportPageAndNoAmount()
    {
        foreach (var email in CreateAll(new DateTime(2026, 11, 15, 14, 2, 0)))
        {
            Assert.Equal(Recipient, email.RecipientAddress);
            Assert.DoesNotContain("€", email.Body);
            Assert.Equal(1, CountOccurrences(email.Body, "http"));
            Assert.EndsWith("écris au support de WalletFlow :\n" + SupportPage, email.Body.Replace("Écris aussi", "écris"));
            Assert.StartsWith("Bonjour,", email.Body);
        }
    }

    // Chaque alerte dit ce qui s'est passé, et a son propre objet.
    [Fact]
    public void EachAlertSaysWhatHappened()
    {
        var all = CreateAll(new DateTime(2026, 11, 16, 10, 30, 0));

        Assert.Contains("depuis un appareil que ton compte ne connaissait pas", all[0].Body);
        Assert.Contains("Le mot de passe de ton compte WalletFlow vient d'être changé", all[1].Body);
        Assert.Contains("La double authentification de ton compte WalletFlow vient d'être désactivée", all[2].Body);
        Assert.Contains("réactive la double authentification", all[2].Body);
        Assert.Contains("Un code de secours de ton compte WalletFlow vient de servir", all[3].Body);
        Assert.Contains("désactive et réactive la double authentification pour recevoir de nouveaux codes de secours", all[3].Body);
        Assert.Equal(4, all.Select(email => email.Subject).Distinct().Count());
        Assert.Equal("La double authentification de ton compte WalletFlow est désactivée", all[2].Subject);
        Assert.Equal("Un code de secours de ton compte WalletFlow a servi", all[3].Subject);
    }

    // Compte les fois où ce morceau apparaît dans le texte.
    private static int CountOccurrences(string text, string part)
    {
        return (text.Length - text.Replace(part, string.Empty).Length) / part.Length;
    }
}
