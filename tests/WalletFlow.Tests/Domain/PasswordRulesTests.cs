using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles du mot de passe (GR Auth, SS §2).
public class PasswordRulesTests
{
    [Theory]
    [InlineData("Abcdefghij1!")] // exactement 12 caractères
    [InlineData("Abcdefghij 1")] // une espace compte comme caractère spécial
    public void PasswordRespectingAllTheRulesIsAccepted(string passwordRespectingAllTheBusinessRules)
    {
        Assert.True(PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordRespectingAllTheBusinessRules));
    }

    [Theory]
    [InlineData("Abcdefghi1!")]  // 11 caractères
    [InlineData("abcdefghij1!")] // sans majuscule
    [InlineData("ABCDEFGHIJ1!")] // sans minuscule
    [InlineData("Abcdefghijk!")] // sans chiffre
    [InlineData("Abcdefghijk1")] // sans caractère spécial
    public void PasswordBreakingOneRuleIsRefused(string passwordBreakingOneOfTheRules)
    {
        Assert.False(PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordBreakingOneOfTheRules));
    }

    [Fact]
    public void PasswordOfExactlyTheMaximumLengthIsAccepted()
    {
        var passwordOfExactlyTheMaximumLength = "Aa1!" + new string('x', 124);

        Assert.Equal(128, passwordOfExactlyTheMaximumLength.Length);
        Assert.True(PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordOfExactlyTheMaximumLength));
    }

    [Fact]
    public void PasswordLongerThanTheMaximumLengthIsRefused()
    {
        var passwordLongerThanTheMaximumLength = "Aa1!" + new string('x', 125);

        Assert.Equal(129, passwordLongerThanTheMaximumLength.Length);
        Assert.False(PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordLongerThanTheMaximumLength));
    }

    // 8 vrais caractères, alors que Length en compte 12 : chaque emoji occupe deux unités en mémoire.
    [Fact]
    public void PasswordOfEightRealCharactersWithEmojisIsRefused()
    {
        var passwordOfEightRealCharactersWithEmojis = "Aa1!😀😀😀😀";

        Assert.Equal(12, passwordOfEightRealCharactersWithEmojis.Length);
        Assert.False(PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordOfEightRealCharactersWithEmojis));
    }

    // Un emoji compte pour un seul caractère, et il compte comme caractère spécial.
    [Fact]
    public void PasswordOfTwelveRealCharactersWithOneEmojiIsAccepted()
    {
        Assert.True(PasswordRules.CheckThatPasswordRespectsAllTheRules("Abcdefghij1😀"));
    }

    // 128 vrais caractères, alors que Length en compte 252.
    [Fact]
    public void PasswordOfExactlyTheMaximumLengthWithEmojisIsAccepted()
    {
        var passwordOfExactlyTheMaximumLengthWithEmojis = "Aa1!" + string.Concat(Enumerable.Repeat("😀", 124));

        Assert.Equal(252, passwordOfExactlyTheMaximumLengthWithEmojis.Length);
        Assert.True(PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordOfExactlyTheMaximumLengthWithEmojis));
    }

    [Fact]
    public void PasswordLongerThanTheMaximumLengthWithEmojisIsRefused()
    {
        var passwordLongerThanTheMaximumLengthWithEmojis = "Aa1!" + string.Concat(Enumerable.Repeat("😀", 125));

        Assert.False(PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordLongerThanTheMaximumLengthWithEmojis));
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
    public void PasswordContainingInvisibleOrUnassignedCharacterIsRefused(int codeOfTheRefusedCharacterForThisTest)
    {
        var passwordThatContainsOneRefusedCharacter = "Abcdefghij1!" + (char)codeOfTheRefusedCharacterForThisTest;

        Assert.False(PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordThatContainsOneRefusedCharacter));
    }

    // Un signe combinant, ajouté à une lettre, n'est pas un caractère spécial.
    [Theory]
    [InlineData(0x0301)] // accent aigu
    [InlineData(0x0903)] // signe combinant avec espacement
    [InlineData(0x20DD)] // cercle autour de la lettre
    public void CombiningAccentDoesNotCountAsSpecialCharacter(int codeOfTheCombiningMarkForThisTest)
    {
        var passwordWithoutAnyRealSpecialCharacter = "Abcdefghije" + (char)codeOfTheCombiningMarkForThisTest + "1";

        Assert.False(PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordWithoutAnyRealSpecialCharacter));
    }

    // L'accent combinant reste permis à côté d'un vrai caractère spécial.
    [Fact]
    public void CombiningAccentIsAllowedNextToRealSpecialCharacter()
    {
        Assert.True(PasswordRules.CheckThatPasswordRespectsAllTheRules("Abcdefghijé1!"));
    }
}
