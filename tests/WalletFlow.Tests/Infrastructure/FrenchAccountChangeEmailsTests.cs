using WalletFlow.Infrastructure.Emails;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le texte des e-mails du changement d'adresse et de la suppression du compte (GR Compte, GR Suppression, SS §9).
public class FrenchAccountChangeEmailsTests
{
    private const string Link = "https://walletflow.test/confirm-email-change#token=jeton-du-lien";
    private const string SupportPage = "https://walletflow.test/support";

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

    // SS §9 : l'e-mail à l'ancienne adresse ne cite aucune adresse e-mail, et invite à écrire au support, avec l'adresse
    // de la page de support seule sur sa ligne. C'est son seul lien.
    [Fact]
    public void OldAddressEmailQuotesNoAddressAndPointsToTheSupportPage()
    {
        var email = emails.CreateEmailChangedEmail("ancienne@exemple.test", SupportPage);

        Assert.Equal("ancienne@exemple.test", email.RecipientAddress);
        Assert.DoesNotContain("@", email.Body);
        Assert.Contains("écris tout de suite au support de WalletFlow :\n" + SupportPage, email.Body);
        Assert.Equal(1, CountOccurrences(email.Body, "http"));
    }

    // GR Suppression : la demande donne le jour de Paris de l'effacement, en toutes lettres, et la façon de changer d'avis. SS §9 :
    // si ce n'est pas l'utilisateur, elle dit quoi faire et finit par la page de support, son seul lien, seule sur sa ligne.
    [Theory]
    [InlineData(2026, 12, 17, "le 17 décembre 2026")]
    [InlineData(2027, 3, 1, "le 1er mars 2027")]
    public void DeletionRequestEmailGivesTheErasureDayAndHowToCancel(int year, int month, int day, string text)
    {
        var email = emails.CreateDeletionRequestedEmail("compte@exemple.test", new DateOnly(year, month, day), SupportPage);

        Assert.Equal(("compte@exemple.test", "La suppression de ton compte WalletFlow est demandée"), (email.RecipientAddress, email.Subject));
        Assert.Contains("effacés définitivement " + text + ".", email.Body);
        Assert.Contains("il te suffit de te reconnecter avant cette date", email.Body);
        Assert.Contains("Toutes tes sessions sont fermées, et l'autorisation donnée à ta banque est révoquée.", email.Body);
        Assert.EndsWith("reconnecte-toi tout de suite pour l'annuler, choisis un nouveau mot de passe avec « Mot de passe oublié », puis écris au support de WalletFlow :\n" + SupportPage, email.Body);
        Assert.Equal(1, CountOccurrences(email.Body, "http"));
        Assert.DoesNotContain("@", email.Body);
    }

    // SS §9 : l'effacement est confirmé à l'adresse du compte effacé, sans aucun lien : il n'y a plus de compte où se connecter.
    // Le délai cité suit la règle des 30 jours.
    [Fact]
    public void ErasureEmailHasNoLinkAndGivesTheDelay()
    {
        var email = emails.CreateAccountErasedEmail("efface@exemple.test");

        Assert.Equal(("efface@exemple.test", "Ton compte WalletFlow est effacé"), (email.RecipientAddress, email.Subject));
        Assert.Contains("Comme tu l'avais demandé il y a 30 jours, ton compte WalletFlow est effacé, avec toutes ses données.", email.Body);
        Assert.Contains("de nos sauvegardes au plus tard dans 30 jours", email.Body);
        Assert.DoesNotContain("http", email.Body);
        Assert.DoesNotContain("@", email.Body);
    }

    // Compte les fois où ce morceau apparaît dans le texte.
    private static int CountOccurrences(string text, string part)
    {
        return (text.Length - text.Replace(part, string.Empty).Length) / part.Length;
    }
}
