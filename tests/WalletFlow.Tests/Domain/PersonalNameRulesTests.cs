using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles du nom et du prénom (GR Compte, DD §3).
public class PersonalNameRulesTests
{
    // Renvoie vrai si le nom tapé, une fois mis sous sa forme enregistrée, respecte les règles.
    private static bool IsTypedNameValid(string typedName)
    {
        return PersonalNameRules.IsValid(PersonalNameRules.Normalize(typedName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \u00A0")]
    public void EmptyNameErasesWhatWasThere(string? typedName)
    {
        var name = PersonalNameRules.Normalize(typedName);

        Assert.Null(name);
        Assert.True(PersonalNameRules.IsValid(name));
    }

    // Tapé sur deux appareils différents, un « É » donne le même nom.
    [Fact]
    public void NameIsTrimmedAndWrittenInNormalizedForm()
    {
        Assert.Equal("Élise", PersonalNameRules.Normalize("  E\u0301lise  "));
    }

    [Theory]
    [InlineData("Dupont")]
    [InlineData("Jean-Pierre")]
    [InlineData("O'Neil")]
    [InlineData("Marie Claire")]
    [InlineData("Ñúñez")]
    [InlineData("李")]
    public void OrdinaryNamesAreAccepted(string typedName)
    {
        Assert.True(IsTypedNameValid(typedName));
    }

    // PostgreSQL compte les caractères, pas les morceaux de .NET : un emoji compte pour un, comme dans la colonne VARCHAR(50).
    [Fact]
    public void FiftyCharactersAreAcceptedAndFiftyOneAreRefused()
    {
        var fiftyEmojis = string.Concat(Enumerable.Repeat("😀", 50));

        Assert.True(IsTypedNameValid(new string('a', 50)));
        Assert.False(IsTypedNameValid(new string('a', 51)));
        Assert.Equal(100, fiftyEmojis.Length);
        Assert.True(IsTypedNameValid(fiftyEmojis));
        Assert.False(IsTypedNameValid(fiftyEmojis + "😀"));
    }

    // Un caractère invisible pourrait tromper un lecteur, ou ne plus se retaper.
    [Theory]
    [InlineData("Du\npont")]
    [InlineData("Du\tpont")]
    [InlineData("Du\u200Bpont")]
    [InlineData("Du\u202Epont")]
    [InlineData("Du\uE000pont")]
    [InlineData("Du\uFFFDpont")]
    public void NameWithInvisibleCharacterIsRefused(string typedName)
    {
        Assert.False(IsTypedNameValid(typedName));
    }

    // Un morceau de caractère mal formé ne se normalise pas : il est gardé tel quel, puis refusé.
    [Fact]
    public void LoneSurrogateIsKeptThenRefused()
    {
        var typedName = "Du" + (char)0xD800 + "pont";

        Assert.Equal(typedName, PersonalNameRules.Normalize(typedName));
        Assert.False(IsTypedNameValid(typedName));
    }
}
