using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles de l'adresse e-mail (DD §3, SS §5).
public class EmailAddressRulesTests
{
    [Fact]
    public void StoredFormHasNoSurroundingSpacesAndNoUppercaseLetter()
    {
        var emailAddressInItsStoredForm = EmailAddressRules.ConvertEmailAddressIntoItsStoredForm("  Prenom.Nom@Exemple.FR ");

        Assert.Equal("prenom.nom@exemple.fr", emailAddressInItsStoredForm);
    }

    [Theory]
    [InlineData("prenom.nom@exemple.fr")]
    [InlineData("prenom+etiquette@exemple.co.uk")]
    [InlineData("prenom_nom-2@mon-exemple.fr")]
    [InlineData("a@b.fr")]
    [InlineData("prenom@exemple.xn--p1ai")] // domaine international, écrit sans accent
    [InlineData("o'neil@exemple.ie")]
    [InlineData("jean.d'arc@exemple.fr")]
    public void EmailAddressWithValidFormatIsAccepted(string validlyFormattedEmailAddressForThisTest)
    {
        Assert.True(EmailAddressRules.CheckThatEmailAddressHasValidFormat(validlyFormattedEmailAddressForThisTest));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sans-arobase.exemple.fr")]
    [InlineData("prenom@")]
    [InlineData("@exemple.fr")]
    [InlineData("prenom@domaine-sans-point")]
    [InlineData("nom <prenom@exemple.fr>")]
    [InlineData("prénom@exemple.fr")]
    [InlineData("prenom@autre@exemple.fr")]
    [InlineData("a@exemple.fr@autre.fr")]
    [InlineData("\"<script>alert(1)</script>\"@exemple.fr")] // texte libre entre guillemets
    [InlineData("\"a b\"@exemple.fr")]
    [InlineData("prenom nom@exemple.fr")]
    [InlineData("a`b@exemple.fr")]
    [InlineData("a\"b@exemple.fr")]
    [InlineData("a\\b@exemple.fr")]
    [InlineData("a<b@exemple.fr")]
    [InlineData("a&b@exemple.fr")]
    [InlineData(".prenom@exemple.fr")]
    [InlineData("prenom.@exemple.fr")]
    [InlineData("prenom..nom@exemple.fr")]
    [InlineData("prenom@exemple.")]
    [InlineData("prenom@.exemple.fr")]
    [InlineData("prenom@exemple..fr")]
    [InlineData("prenom@-exemple.fr")]
    [InlineData("prenom@exemple-.fr")]
    [InlineData("prenom@exem_ple.fr")]
    [InlineData("a@[127.0.0.1]")]
    [InlineData("a@127.0.0.1")]
    [InlineData("prenom@exémple.fr")]
    [InlineData("a@b.c")]
    [InlineData("a@exemple.1a")]
    public void EmailAddressWithInvalidFormatIsRefused(string badlyFormattedEmailAddressForThisTest)
    {
        Assert.False(EmailAddressRules.CheckThatEmailAddressHasValidFormat(badlyFormattedEmailAddressForThisTest));
    }

    // Un caractère invisible est refusé, même entre guillemets : PostgreSQL ne sait pas enregistrer le caractère NUL.
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x0A)]
    [InlineData(0x7F)]
    public void EmailAddressContainingInvisibleControlCharacterIsRefused(int codeOfTheInvisibleControlCharacter)
    {
        var invisibleControlCharacterForThisTest = (char)codeOfTheInvisibleControlCharacter;

        Assert.False(EmailAddressRules.CheckThatEmailAddressHasValidFormat("a" + invisibleControlCharacterForThisTest + "@exemple.fr"));
        Assert.False(EmailAddressRules.CheckThatEmailAddressHasValidFormat("\"a\\" + invisibleControlCharacterForThisTest + "\"@exemple.fr"));
    }

    [Fact]
    public void TextBeforeTheAtSignOfExactlyTheMaximumLengthIsAccepted()
    {
        Assert.True(EmailAddressRules.CheckThatEmailAddressHasValidFormat(new string('a', 64) + "@exemple.fr"));
    }

    [Fact]
    public void TextBeforeTheAtSignLongerThanTheMaximumLengthIsRefused()
    {
        Assert.False(EmailAddressRules.CheckThatEmailAddressHasValidFormat(new string('a', 65) + "@exemple.fr"));
    }

    [Fact]
    public void DomainPartOfExactlyTheMaximumLengthIsAccepted()
    {
        Assert.True(EmailAddressRules.CheckThatEmailAddressHasValidFormat("a@" + new string('b', 63) + ".fr"));
    }

    [Fact]
    public void DomainPartLongerThanTheMaximumLengthIsRefused()
    {
        Assert.False(EmailAddressRules.CheckThatEmailAddressHasValidFormat("a@" + new string('b', 64) + ".fr"));
    }

    [Fact]
    public void EmailAddressOfExactlyTheMaximumLengthIsAccepted()
    {
        var emailAddressOfExactlyTheMaximumLength = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 58) + ".fr";

        Assert.Equal(254, emailAddressOfExactlyTheMaximumLength.Length);
        Assert.True(EmailAddressRules.CheckThatEmailAddressHasValidFormat(emailAddressOfExactlyTheMaximumLength));
    }

    [Fact]
    public void EmailAddressLongerThanTheMaximumLengthIsRefused()
    {
        var emailAddressLongerThanTheMaximumLength = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 59) + ".fr";

        Assert.Equal(255, emailAddressLongerThanTheMaximumLength.Length);
        Assert.False(EmailAddressRules.CheckThatEmailAddressHasValidFormat(emailAddressLongerThanTheMaximumLength));
    }
}
