using WalletFlow.Infrastructure.Emails;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le texte en français des alertes de sécurité (GR Notif, SS §9).
public class FrenchSecurityAlertEmailsTests
{
    private const string Recipient = "utilisateur@exemple.test";

    private readonly FrenchSecurityAlertEmails emails = new FrenchSecurityAlertEmails();

    // Le premier du mois s'écrit « 1er », et les minutes ont toujours deux chiffres.
    [Theory]
    [InlineData(2026, 11, 1, 9, 5, "le 1er novembre 2026 à 9 h 05")]
    [InlineData(2026, 12, 31, 23, 59, "le 31 décembre 2026 à 23 h 59")]
    [InlineData(2027, 8, 2, 0, 0, "le 2 août 2027 à 0 h 00")]
    public void MomentIsWrittenInFrenchWords(int year, int month, int day, int hour, int minute, string text)
    {
        var moment = new DateTime(year, month, day, hour, minute, 0);

        Assert.Contains(text + " (heure de Paris)", emails.CreateNewDeviceEmail(Recipient, moment).Body);
        Assert.Contains(text + " (heure de Paris)", emails.CreatePasswordChangedEmail(Recipient, moment).Body);
    }

    // Une alerte ne cite aucun montant (GR Notif) et ne contient aucun lien : elle ne sert jamais à se connecter.
    [Fact]
    public void AlertsContainNeitherLinkNorAmount()
    {
        var moment = new DateTime(2026, 11, 15, 14, 2, 0);

        foreach (var email in new[] { emails.CreateNewDeviceEmail(Recipient, moment), emails.CreatePasswordChangedEmail(Recipient, moment) })
        {
            Assert.Equal(Recipient, email.RecipientAddress);
            Assert.DoesNotContain("http", email.Body);
            Assert.DoesNotContain("€", email.Body);
            Assert.Contains("support", email.Body);
        }
    }
}
