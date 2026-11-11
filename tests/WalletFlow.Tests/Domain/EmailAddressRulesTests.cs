using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles de l'adresse e-mail (DD §3, SS §5).
public class EmailAddressRulesTests
{
    [Fact]
    public void StoredFormHasNoSurroundingSpacesAndNoUppercaseLetter()
    {
        var email = EmailAddressRules.Normalize("  Prenom.Nom@Exemple.FR ");

        Assert.Equal("prenom.nom@exemple.fr", email);
    }

    [Theory]
    [InlineData("prenom.nom@exemple.fr")]
    [InlineData("prenom+etiquette@exemple.co.uk")]
    [InlineData("prenom_nom-2@mon-exemple.fr")]
    [InlineData("a@b.fr")]
    [InlineData("prenom@exemple.xn--p1ai")] // domaine international, écrit sans accent
    [InlineData("o'neil@exemple.ie")]
    [InlineData("jean.d'arc@exemple.fr")]
    public void EmailAddressWithValidFormatIsAccepted(string validEmail)
    {
        Assert.True(EmailAddressRules.IsValid(validEmail));
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
    public void EmailAddressWithInvalidFormatIsRefused(string invalidEmail)
    {
        Assert.False(EmailAddressRules.IsValid(invalidEmail));
    }

    // Un caractère invisible est refusé, même entre guillemets : PostgreSQL ne sait pas enregistrer le caractère NUL.
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x0A)]
    [InlineData(0x7F)]
    public void EmailAddressContainingInvisibleControlCharacterIsRefused(int controlCharacterCode)
    {
        var controlCharacter = (char)controlCharacterCode;

        Assert.False(EmailAddressRules.IsValid("a" + controlCharacter + "@exemple.fr"));
        Assert.False(EmailAddressRules.IsValid("\"a\\" + controlCharacter + "\"@exemple.fr"));
    }

    [Fact]
    public void TextBeforeTheAtSignOfExactlyTheMaximumLengthIsAccepted()
    {
        Assert.True(EmailAddressRules.IsValid(new string('a', 64) + "@exemple.fr"));
    }

    [Fact]
    public void TextBeforeTheAtSignLongerThanTheMaximumLengthIsRefused()
    {
        Assert.False(EmailAddressRules.IsValid(new string('a', 65) + "@exemple.fr"));
    }

    [Fact]
    public void DomainPartOfExactlyTheMaximumLengthIsAccepted()
    {
        Assert.True(EmailAddressRules.IsValid("a@" + new string('b', 63) + ".fr"));
    }

    [Fact]
    public void DomainPartLongerThanTheMaximumLengthIsRefused()
    {
        Assert.False(EmailAddressRules.IsValid("a@" + new string('b', 64) + ".fr"));
    }

    [Fact]
    public void EmailAddressOfExactlyTheMaximumLengthIsAccepted()
    {
        var longestEmail = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 58) + ".fr";

        Assert.Equal(254, longestEmail.Length);
        Assert.True(EmailAddressRules.IsValid(longestEmail));
    }

    [Fact]
    public void EmailAddressLongerThanTheMaximumLengthIsRefused()
    {
        var tooLongEmail = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 59) + ".fr";

        Assert.Equal(255, tooLongEmail.Length);
        Assert.False(EmailAddressRules.IsValid(tooLongEmail));
    }
}
