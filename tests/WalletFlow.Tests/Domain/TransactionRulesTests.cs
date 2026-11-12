using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles des opérations reçues de la banque (GR Comptes, SS §5, DD §5.3).
// Les caractères spéciaux sont écrits par leur code, pour que le fichier reste lisible.
public class TransactionRulesTests
{
    private static readonly DateOnly Today = new DateOnly(2026, 11, 12);

    [Fact]
    public void LimitsAreTheOnesOfTheDocuments()
    {
        Assert.Equal(255, TransactionRules.MaximumLabelLength);
        Assert.Equal(1_000_000_000, TransactionRules.MaximumAmountInCents);
        Assert.Equal(100, TransactionRules.MaximumExternalIdLength);
        Assert.Equal(new DateOnly(2000, 1, 1), TransactionRules.EarliestDate);
        Assert.Equal(365, TransactionRules.FirstReadingDays);
        Assert.Equal(7, TransactionRules.ReadingOverlapDays);
    }

    // DD §5.3 : un montant non nul, au plus 1 000 000 000 centimes en valeur absolue, sans débordement aux extrêmes.
    [Theory]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    [InlineData(1_000_000_000, true)]
    [InlineData(-1_000_000_000, true)]
    [InlineData(0, false)]
    [InlineData(1_000_000_001, false)]
    [InlineData(-1_000_000_001, false)]
    [InlineData(long.MaxValue, false)]
    [InlineData(long.MinValue, false)]
    public void AmountIsNotZeroAndStaysWithinTheLimits(long amountInCents, bool isValid)
    {
        Assert.Equal(isValid, TransactionRules.IsAmountValid(amountInCents));
    }

    [Fact]
    public void DateStartsOnTheFirstOfJanuaryTwoThousand()
    {
        Assert.True(TransactionRules.IsDateValid(new DateOnly(2000, 1, 1)));
        Assert.False(TransactionRules.IsDateValid(new DateOnly(1999, 12, 31)));
    }

    // Un compte sans opération est lu sur un an ; ensuite, depuis sa dernière opération moins 7 jours.
    [Fact]
    public void ReadingStartsOneYearBackThenAWeekBeforeTheLatestOperation()
    {
        Assert.Equal(Today.AddDays(-365), TransactionRules.ChooseReadingStart(null, Today));
        Assert.Equal(new DateOnly(2026, 11, 3), TransactionRules.ChooseReadingStart(new DateOnly(2026, 11, 10), Today));
        Assert.Equal(Today.AddDays(-7), TransactionRules.ChooseReadingStart(Today, Today));
    }

    // Une connexion restée longtemps expirée ne fait jamais remonter la lecture plus d'un an en arrière.
    [Fact]
    public void ReadingNeverStartsMoreThanOneYearBack()
    {
        Assert.Equal(Today.AddDays(-365), TransactionRules.ChooseReadingStart(new DateOnly(2024, 1, 1), Today));
        Assert.Equal(Today.AddDays(-365), TransactionRules.ChooseReadingStart(Today.AddDays(-358), Today));
        Assert.Equal(Today.AddDays(-364), TransactionRules.ChooseReadingStart(Today.AddDays(-357), Today));
    }

    // Une opération datée dans le futur ne fait jamais demander un jour futur : la banque refuserait toutes les lectures suivantes.
    [Fact]
    public void ReadingNeverStartsAfterToday()
    {
        Assert.Equal(Today, TransactionRules.ChooseReadingStart(Today.AddDays(30), Today));
        Assert.Equal(Today, TransactionRules.ChooseReadingStart(Today.AddDays(7), Today));
        Assert.Equal(Today.AddDays(-1), TransactionRules.ChooseReadingStart(Today.AddDays(6), Today));
    }

    // SS §5 : chaque suite d'espaces, de tabulations ou de retours à la ligne devient un seul espace, et rien autour.
    [Fact]
    public void LabelSpacesAreJoinedIntoOne()
    {
        var label = "  CB" + (char)0x09 + "CARREFOUR" + (char)0x0A + (char)0x0D + " " + (char)0x00A0 + "12/11  ";

        Assert.Equal("CB CARREFOUR 12/11", TransactionRules.CleanLabel(label));
    }

    // SS §5 : un caractère invisible disparaît sans coller les mots ni doubler les espaces.
    [Fact]
    public void InvisibleCharactersDisappearWithoutGluingWords()
    {
        var zeroWidthSpace = ((char)0x200B).ToString();
        var rightToLeftOverride = ((char)0x202E).ToString();
        var label = "CB" + zeroWidthSpace + " " + zeroWidthSpace + "CARREFOUR" + rightToLeftOverride + " MARKET" + (char)0xD800;

        Assert.Equal("CB CARREFOUR MARKET", TransactionRules.CleanLabel(label));
        Assert.Equal("CB CARREFOUR", TransactionRules.CleanLabel("CB " + zeroWidthSpace + " CARREFOUR"));
    }

    // DD §5.3 : 255 caractères au plus, comptés comme PostgreSQL : un emoji compte pour un, et n'est jamais coupé en deux.
    [Fact]
    public void LabelIsCutAtTwoHundredFiftyFiveCharacters()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        var label = TransactionRules.CleanLabel(string.Concat(Enumerable.Repeat(emoji, 300)));

        Assert.Equal(255, label.EnumerateRunes().Count());
        Assert.Equal(string.Concat(Enumerable.Repeat(emoji, 255)), label);
    }

    // Les accents sont gardés, en Unicode NFC, comme les noms reçus de la banque.
    [Fact]
    public void LabelIsWrittenInNfc()
    {
        var decomposedE = "E" + (char)0x0301;

        Assert.Equal(((char)0x00C9).ToString() + "PICERIE", TransactionRules.CleanLabel(decomposedE + "PICERIE"));
    }

    // DD §5.3 : un libellé fait au moins un caractère. Sans rien de visible, il devient « Opération bancaire ».
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void EmptyLabelIsReplaced(string? label)
    {
        Assert.Equal("Opération bancaire", TransactionRules.CleanLabel(label));
    }
}
