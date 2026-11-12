using WalletFlow.Infrastructure.Emails;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le texte des e-mails du changement d'adresse (GR Compte, SS §9).
public class FrenchAccountChangeEmailsTests
{
    private const string Link = "https://walletflow.test/confirm-email-change#token=jeton-du-lien";

    private readonly FrenchAccountChangeEmails emails = new FrenchAccountChangeEmails();

    // Le lien part vers la nouvelle adresse, avec sa durée de validité et la consigne de saisir le mot de passe.
    [Fact]
    public void LinkEmailGivesTheLinkItsDurationAndWhatToDo()
    {
        var email = emails.CreateEmailChangeLinkEmail("nouvelle@exemple.test", Link);

        Assert.Equal("nouvelle@exemple.test", email.RecipientAddress);
        Assert.Contains(Link, email.Body);
        Assert.Contains("24 heures", email.Body);
        Assert.Contains("mot de passe", email.Body);
    }

    // Le propriétaire d'une adresse déjà utilisée est prévenu, sans aucun lien.
    [Fact]
    public void AddressAlreadyUsedEmailHasNoLink()
    {
        var email = emails.CreateAddressAlreadyUsedEmail("deja-prise@exemple.test");

        Assert.Equal("deja-prise@exemple.test", email.RecipientAddress);
        Assert.DoesNotContain("http", email.Body);
    }

    // SS §9 : l'e-mail à l'ancienne adresse ne cite aucune adresse, et invite à écrire au support.
    [Fact]
    public void OldAddressEmailQuotesNoAddressAndPointsToTheSupport()
    {
        var email = emails.CreateEmailChangedEmail("ancienne@exemple.test");

        Assert.Equal("ancienne@exemple.test", email.RecipientAddress);
        Assert.DoesNotContain("@", email.Body);
        Assert.Contains("support", email.Body);
    }
}
