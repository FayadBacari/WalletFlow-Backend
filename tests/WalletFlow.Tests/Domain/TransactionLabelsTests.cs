using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie la forme simple d'un libellé, qui reconnaît « le même libellé » (GR Dépenses, DD §6.2).
// Les caractères spéciaux sont écrits par leur code, pour que le fichier reste lisible.
public class TransactionLabelsTests
{
    // DD §6.2 : sans les chiffres, la ponctuation, les majuscules, les accents ni les espaces en trop.
    [Theory]
    [InlineData("CB Carrefour 12/11", "CB CARREFOUR")]
    [InlineData("CB CARREFOUR 03/12", "CB CARREFOUR")]
    [InlineData("  prlv   sepa  EDF-clients  ", "PRLV SEPA EDF CLIENTS")]
    [InlineData("MCDONALD'S 75012", "MCDONALD S")]
    [InlineData("H&M", "H M")]
    [InlineData("VIR. SALAIRE (NOV.)", "VIR SALAIRE NOV")]
    public void SimpleFormKeepsOnlyTheLettersInCapitals(string label, string expectedSimpleLabel)
    {
        Assert.Equal(expectedSimpleLabel, TransactionLabels.Simplify(label));
    }

    // Les accents disparaissent, écrits en un seul caractère ou en deux.
    [Fact]
    public void AccentsDisappear()
    {
        var composed = ((char)0x00C9).ToString() + "picerie " + (char)0x00E9 + "l" + (char)0x00E8 + "ve";
        var decomposed = "E" + (char)0x0301 + "picerie e" + (char)0x0301 + "le" + (char)0x0300 + "ve";

        Assert.Equal("EPICERIE ELEVE", TransactionLabels.Simplify(composed));
        Assert.Equal("EPICERIE ELEVE", TransactionLabels.Simplify(decomposed));
    }

    // Un libellé sans aucune lettre n'a pas de forme simple : aucune règle ne peut le reconnaître.
    [Theory]
    [InlineData("12345")]
    [InlineData("12/11 - 45,00")]
    [InlineData("")]
    [InlineData("   ")]
    public void LabelWithoutLetterHasAnEmptySimpleForm(string label)
    {
        Assert.Equal(string.Empty, TransactionLabels.Simplify(label));
    }

    // Les lettres des autres alphabets restent des lettres.
    [Fact]
    public void LettersOfOtherAlphabetsAreKept()
    {
        var greekAlpha = ((char)0x03B1).ToString();
        var greekCapitalAlpha = ((char)0x0391).ToString();

        Assert.Equal(greekCapitalAlpha + " CAFE", TransactionLabels.Simplify(greekAlpha + "-caf" + (char)0x00E9));
    }

    // Un texte mal formé ne fait jamais échouer la forme simple : son morceau de caractère devient un espace.
    [Fact]
    public void MalformedLabelDoesNotFail()
    {
        Assert.Equal("AB CD", TransactionLabels.Simplify("ab" + (char)0xD800 + "cd"));
    }

    // Une règle commune reconnaît des mots entiers, jamais un morceau de mot.
    [Theory]
    [InlineData("CB CARREFOUR MARKET", "CARREFOUR", true)]
    [InlineData("CARREFOUR", "CARREFOUR", true)]
    [InlineData("CB CARREFOUR", "CARREFOUR", true)]
    [InlineData("CB CARREFOURS", "CARREFOUR", false)]
    [InlineData("CB HYPERCARREFOUR", "CARREFOUR", false)]
    [InlineData("PAIEMENT UBER EATS PARIS", "UBER EATS", true)]
    [InlineData("PAIEMENT UBER PARIS EATS", "UBER EATS", false)]
    [InlineData("CB CARREFOUR", "", false)]
    public void WordsAreFoundOnlyWhole(string simpleLabel, string simpleWords, bool isFound)
    {
        Assert.Equal(isFound, TransactionLabels.ContainsWords(simpleLabel, simpleWords));
    }
}
