using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles d'un sac (GR Sacs, SS §5 et §6, DD §7 et §11).
public class BagRulesTests
{
    private static readonly DateOnly Today = new DateOnly(2026, 11, 12);

    [Fact]
    public void LimitsAreTheChosenOnes()
    {
        Assert.Equal(100, BagRules.MaximumCountPerUser);
        Assert.Equal(1_000_000_000, BagRules.MaximumAmountInCents);
        Assert.Equal(1000, BagRules.MaximumMovementsPerDay);
        Assert.Equal(TimeSpan.FromHours(24), BagRules.MovementDay);
    }

    // SS §5 : le nom d'un sac suit la règle du nom d'une catégorie. Un emoji compte pour un caractère, comme dans PostgreSQL.
    [Fact]
    public void NameHasOneToFiftyCharactersWithoutInvisibleCharacter()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);

        Assert.True(BagRules.IsNameValid("V"));
        Assert.True(BagRules.IsNameValid(new string('a', 50)));
        Assert.True(BagRules.IsNameValid(string.Concat(Enumerable.Repeat(emoji, 50))));
        Assert.False(BagRules.IsNameValid(string.Empty));
        Assert.False(BagRules.IsNameValid(new string('a', 51)));
        Assert.False(BagRules.IsNameValid(string.Concat(Enumerable.Repeat(emoji, 51))));
        Assert.False(BagRules.IsNameValid("Vacances" + (char)0x200B));
    }

    // Le nom enregistré est en Unicode NFC, sans espaces autour : « É » écrit en deux caractères devient un seul.
    [Fact]
    public void NameIsStoredInNfcWithoutSpacesAround()
    {
        var decomposedName = "  E" + (char)0x0301 + "pargne ";
        var composedName = (char)0x00C9 + "pargne";

        Assert.Equal(composedName, BagRules.NormalizeName(decomposedName));
        Assert.Equal(string.Empty, BagRules.NormalizeName(null));
    }

    // DD §7 et SS §5 : un montant va de 1 centime à 1 000 000 000 centimes.
    [Theory]
    [InlineData(1, true)]
    [InlineData(1_000_000_000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1_000_000_001, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, false)]
    public void AmountGoesFromOneCentToTheMaximum(int amountInCents, bool expected)
    {
        Assert.Equal(expected, BagRules.IsAmountValid(amountInCents));
    }

    // GR Sacs et DD §7.1 : un montant à atteindre, une date facultative qui va avec un montant, après le jour de la saisie.
    // Le montant est vérifié d'abord, puis la présence du montant, puis la date.
    [Theory]
    [InlineData(null, null, BagGoalProblem.None)]
    [InlineData(1, null, BagGoalProblem.None)]
    [InlineData(1_000_000_000, null, BagGoalProblem.None)]
    [InlineData(500, "2026-11-13", BagGoalProblem.None)]
    [InlineData(500, "9999-12-31", BagGoalProblem.None)]
    [InlineData(0, null, BagGoalProblem.AmountBreaksRules)]
    [InlineData(1_000_000_001, null, BagGoalProblem.AmountBreaksRules)]
    [InlineData(-1, "2026-11-11", BagGoalProblem.AmountBreaksRules)]
    [InlineData(null, "2027-01-01", BagGoalProblem.DateWithoutAmount)]
    [InlineData(null, "2026-11-12", BagGoalProblem.DateWithoutAmount)]
    [InlineData(500, "2026-11-12", BagGoalProblem.DateTooEarly)]
    [InlineData(500, "2026-11-11", BagGoalProblem.DateTooEarly)]
    public void GoalProblemIsFound(int? targetAmountInCents, string? targetDate, BagGoalProblem expected)
    {
        DateOnly? date = targetDate == null ? null : DateOnly.Parse(targetDate);

        Assert.Equal(expected, BagRules.FindGoalProblem(targetAmountInCents, date, Today));
    }

    // DD §11 : la progression est arrondie vers le bas, peut dépasser 100 %, et s'arrête au plus grand int.
    [Theory]
    [InlineData(0, 1000, 0)]
    [InlineData(999, 1000, 99)]
    [InlineData(1000, 1000, 100)]
    [InlineData(1500, 1000, 150)]
    [InlineData(2, 3, 66)]
    [InlineData(21_474_836, 1, 2_147_483_600)]
    [InlineData(21_474_837, 1, int.MaxValue)]
    [InlineData(3_000_000_000, 1, int.MaxValue)]
    public void ProgressIsRoundedDownAndCanGoBeyondHundred(long balanceInCents, int targetAmountInCents, int expectedPercent)
    {
        Assert.Equal<int?>(expectedPercent, BagRules.CountProgressPercent(balanceInCents, targetAmountInCents));
    }

    [Fact]
    public void BagWithoutGoalHasNoProgress()
    {
        Assert.Null(BagRules.CountProgressPercent(5000, null));
    }
}
