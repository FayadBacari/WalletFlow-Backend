using WalletFlow.Application.Emails;
using WalletFlow.Infrastructure.Emails;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le texte en français des e-mails : chaque e-mail va à son destinataire, chaque lien donne sa vraie durée,
// et la durée du blocage s'écrit en toutes lettres.
public class FrenchAccountEmailsTests
{
    private const string Recipient = "visiteur@exemple.test";
    private const string Link = "https://walletflow.test/page#token=jeton-du-test";

    private readonly FrenchAccountRegistrationEmails registrationEmails = new FrenchAccountRegistrationEmails();
    private readonly FrenchAccountRecoveryEmails recoveryEmails = new FrenchAccountRecoveryEmails();

    // Les durées viennent des règles des liens (24 heures, 30 minutes, 1 heure) : le texte ne peut pas annoncer une autre durée.
    [Fact]
    public void EveryLinkEmailGivesTheRealValidityOfItsLink()
    {
        Assert.Contains(
            "Ce lien est valable 24 heures et ne sert qu'une seule fois.",
            registrationEmails.CreateConfirmationEmail(Recipient, Link).Body);
        Assert.Contains(
            "Ce lien est valable 30 minutes et ne sert qu'une seule fois.",
            recoveryEmails.CreateRecoveryEmail(Recipient, Link).Body);
        Assert.Contains(
            "Ce lien est valable 1 heure et ne sert qu'une seule fois.",
            recoveryEmails.CreateUnlockEmail(Recipient, Link, TimeSpan.FromMinutes(15)).Body);
    }

    // Une durée qui ne compte pas un nombre entier d'heures s'écrit en minutes, sans rien perdre.
    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(15, "15 minutes")]
    [InlineData(30, "30 minutes")]
    [InlineData(60, "1 heure")]
    [InlineData(90, "90 minutes")]
    [InlineData(120, "2 heures")]
    [InlineData(1440, "24 heures")]
    public void LockoutDurationIsWrittenInFrenchWords(int minutes, string expectedText)
    {
        var email = recoveryEmails.CreateUnlockEmail(Recipient, Link, TimeSpan.FromMinutes(minutes));

        Assert.Contains("ton compte WalletFlow est bloqué pendant " + expectedText + ".", email.Body);
    }

    [Fact]
    public void EveryEmailGoesToItsRecipientAndOnlyLinkEmailsCarryALink()
    {
        var linkEmails = new List<EmailMessage>
        {
            registrationEmails.CreateConfirmationEmail(Recipient, Link),
            recoveryEmails.CreateRecoveryEmail(Recipient, Link),
            recoveryEmails.CreateUnlockEmail(Recipient, Link, TimeSpan.FromMinutes(15))
        };
        var emailsWithoutLink = new List<EmailMessage>
        {
            registrationEmails.CreateAddressAlreadyUsedEmail(Recipient),
            recoveryEmails.CreateSignupInvitationEmail(Recipient)
        };

        foreach (var email in linkEmails)
        {
            Assert.Equal(Recipient, email.RecipientAddress);
            Assert.False(string.IsNullOrWhiteSpace(email.Subject));
            Assert.Contains(Link, email.Body);
        }

        foreach (var email in emailsWithoutLink)
        {
            Assert.Equal(Recipient, email.RecipientAddress);
            Assert.False(string.IsNullOrWhiteSpace(email.Subject));
            Assert.DoesNotContain("#token=", email.Body);
        }
    }
}
