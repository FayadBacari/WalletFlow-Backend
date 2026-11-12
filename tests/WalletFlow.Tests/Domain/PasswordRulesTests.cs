using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles du mot de passe (GR Auth, SS §2).
public class PasswordRulesTests
{
    // Tapé sur deux appareils différents, le même mot de passe donne le même résultat (SS §2).
    [Fact]
    public void ComposedAndDecomposedAccentsGiveTheSameNormalizedPassword()
    {
        var composedPassword = "Mot-De-Passé-1";
        var decomposedPassword = "Mot-De-Passe\u0301-1";

        Assert.NotEqual(composedPassword, decomposedPassword);
        Assert.Equal(composedPassword, PasswordRules.Normalize(decomposedPassword));
        Assert.Equal(composedPassword, PasswordRules.Normalize(composedPassword));
    }

    // .NET lève une erreur sur ces caractères : sans ce contrôle, ils provoqueraient une erreur 500 à la connexion.
    [Theory]
    [InlineData(0xFFFE)] // caractère interdit par Unicode
    [InlineData(0xD800)] // moitié d'emoji seule
    [InlineData(0xDC00)] // autre moitié d'emoji seule
    public void TextThatCannotBeNormalizedIsKeptWithoutAnyError(int unnormalizableCharacter)
    {
        var unnormalizablePassword = "Abcdefghij1!" + (char)unnormalizableCharacter;

        Assert.Equal(unnormalizablePassword, PasswordRules.Normalize(unnormalizablePassword));
    }

    [Theory]
    [InlineData("Abcdefghij1!")] // exactement 12 caractères
    [InlineData("Abcdefghij 1")] // une espace compte comme caractère spécial
    public void PasswordRespectingAllTheRulesIsAccepted(string validPassword)
    {
        Assert.True(PasswordRules.IsValid(validPassword));
    }

    [Theory]
    [InlineData("Abcdefghi1!")]  // 11 caractères
    [InlineData("abcdefghij1!")] // sans majuscule
    [InlineData("ABCDEFGHIJ1!")] // sans minuscule
    [InlineData("Abcdefghijk!")] // sans chiffre
    [InlineData("Abcdefghijk1")] // sans caractère spécial
    public void PasswordBreakingOneRuleIsRefused(string invalidPassword)
    {
        Assert.False(PasswordRules.IsValid(invalidPassword));
    }

    [Fact]
    public void PasswordOfExactlyTheMaximumLengthIsAccepted()
    {
        var longestPassword = "Aa1!" + new string('x', 124);

        Assert.Equal(128, longestPassword.Length);
        Assert.True(PasswordRules.IsValid(longestPassword));
    }

    [Fact]
    public void PasswordLongerThanTheMaximumLengthIsRefused()
    {
        var tooLongPassword = "Aa1!" + new string('x', 125);

        Assert.Equal(129, tooLongPassword.Length);
        Assert.False(PasswordRules.IsValid(tooLongPassword));
    }

    // 8 vrais caractères, alors que Length en compte 12 : chaque emoji occupe deux unités en mémoire.
    [Fact]
    public void PasswordOfEightRealCharactersWithEmojisIsRefused()
    {
        var emojiPassword = "Aa1!😀😀😀😀";

        Assert.Equal(12, emojiPassword.Length);
        Assert.False(PasswordRules.IsValid(emojiPassword));
    }

    // Un emoji compte pour un seul caractère, et il compte comme caractère spécial.
    [Fact]
    public void PasswordOfTwelveRealCharactersWithOneEmojiIsAccepted()
    {
        Assert.True(PasswordRules.IsValid("Abcdefghij1😀"));
    }

    // 128 vrais caractères, alors que Length en compte 252.
    [Fact]
    public void PasswordOfExactlyTheMaximumLengthWithEmojisIsAccepted()
    {
        var longestEmojiPassword = "Aa1!" + string.Concat(Enumerable.Repeat("😀", 124));

        Assert.Equal(252, longestEmojiPassword.Length);
        Assert.True(PasswordRules.IsValid(longestEmojiPassword));
    }

    [Fact]
    public void PasswordLongerThanTheMaximumLengthWithEmojisIsRefused()
    {
        var tooLongEmojiPassword = "Aa1!" + string.Concat(Enumerable.Repeat("😀", 125));

        Assert.False(PasswordRules.IsValid(tooLongEmojiPassword));
    }

    // Un caractère invisible ou non attribué est refusé, même si le mot de passe respecte toutes les autres règles.
    [Theory]
    [InlineData(0x0000)] // NUL
    [InlineData(0x0001)] // caractère de contrôle
    [InlineData(0x000A)] // retour à la ligne
    [InlineData(0x007F)] // suppression
    [InlineData(0x200B)] // espace de largeur nulle
    [InlineData(0x200D)] // liant des emojis composés
    [InlineData(0x2028)] // séparateur de lignes
    [InlineData(0x2029)] // séparateur de paragraphes
    [InlineData(0xE000)] // zone à usage privé
    [InlineData(0x0378)] // caractère non attribué par Unicode
    public void PasswordContainingInvisibleOrUnassignedCharacterIsRefused(int refusedCharacterCode)
    {
        var password = "Abcdefghij1!" + (char)refusedCharacterCode;

        Assert.False(PasswordRules.IsValid(password));
    }

    // Un signe combinant, ajouté à une lettre, n'est pas un caractère spécial.
    [Theory]
    [InlineData(0x0301)] // accent aigu
    [InlineData(0x0903)] // signe combinant avec espacement
    [InlineData(0x20DD)] // cercle autour de la lettre
    public void CombiningAccentDoesNotCountAsSpecialCharacter(int combiningMarkCode)
    {
        var password = "Abcdefghije" + (char)combiningMarkCode + "1";

        Assert.False(PasswordRules.IsValid(password));
    }

    // L'accent combinant reste permis à côté d'un vrai caractère spécial.
    [Fact]
    public void CombiningAccentIsAllowedNextToRealSpecialCharacter()
    {
        Assert.True(PasswordRules.IsValid("Abcdefghije\u03011!"));
    }
}
