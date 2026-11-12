using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie la règle des caractères invisibles, commune aux mots de passe et aux noms (DD §3).
public class InvisibleCharactersTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("\u200B")]
    [InlineData("\u2028")]
    [InlineData("\uE000")]
    [InlineData("\uFFFD")]
    [InlineData("\u0378")]
    public void InvisibleCharacterIsFound(string character)
    {
        Assert.True(InvisibleCharacters.AreIn("a" + character + "b"));
    }

    [Theory]
    [InlineData("Élise Dupont-Martin")]
    [InlineData("😀")]
    [InlineData("Mot-De-Passe-Valide-1")]
    public void VisibleTextHasNoInvisibleCharacter(string text)
    {
        Assert.False(InvisibleCharacters.AreIn(text));
    }

    // .NET lisait un morceau de caractère mal formé comme un caractère spécial : il ne peut plus compléter un mot de passe.
    [Fact]
    public void PasswordWithLoneSurrogateIsRefused()
    {
        var password = "Abcdefghij1" + (char)0xD800;

        Assert.False(PasswordRules.IsValid(PasswordRules.Normalize(password)));
    }
}
