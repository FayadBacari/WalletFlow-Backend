using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles fixes des suggestions (GR Sacs, DD §8 et §12) : le mois évalué, ce que chaque règle fait économiser,
// les seuils exacts de 20 %, et la pause de 30 jours.
public class SuggestionRulesTests
{
    [Fact]
    public void CalculationWaitsUntilTheFifth()
    {
        Assert.Equal(5, SuggestionRules.CalculationDay);
    }

    // Le mois d'avant est évalué à partir du 5 : du 1er au 4, c'est encore celui d'avant (GR Sacs, DD §8 et §12).
    [Theory]
    [InlineData("2026-11-05", "2026-10-01")]
    [InlineData("2026-11-30", "2026-10-01")]
    [InlineData("2026-11-04", "2026-09-01")]
    [InlineData("2026-11-01", "2026-09-01")]
    [InlineData("2026-01-05", "2025-12-01")]
    [InlineData("2026-01-04", "2025-11-01")]
    [InlineData("2026-02-03", "2025-12-01")]
    [InlineData("2026-03-31", "2026-02-01")]
    public void EvaluatedMonthStartsOnTheFifth(string today, string expectedMonth)
    {
        Assert.Equal(DateOnly.Parse(expectedMonth), SuggestionRules.FindEvaluatedMonth(DateOnly.Parse(today)));
    }

    // Moyenne de 100 € sur juillet, août et septembre : le seuil est 120 €. À 120 € pile, rien ; un centime de plus,
    // la suggestion propose de revenir à la moyenne.
    [Theory]
    [InlineData(-12_000, null)]
    [InlineData(-12_001, 2_001)]
    [InlineData(-25_000, 15_000)]
    public void OverrunStartsJustAboveTwentyPercent(long monthNetInCents, int? expectedSavings)
    {
        Assert.Equal(expectedSavings, SuggestionRules.FindCategoryOverrunSavings(monthNetInCents, -30_000));
    }

    // Seule une catégorie dont la moyenne est une dépense compte, et seulement un mois où elle dépense.
    [Theory]
    [InlineData(-5_000, 0)]
    [InlineData(-5_000, 30_000)]
    [InlineData(4_000, -30_000)]
    [InlineData(0, -30_000)]
    public void OnlyASpendingCategoryCanOverrun(long monthNetInCents, long previousMonthsNetInCents)
    {
        Assert.Null(SuggestionRules.FindCategoryOverrunSavings(monthNetInCents, previousMonthsNetInCents));
    }

    // La moyenne est arrondie au centime : 10 centimes sur 3 mois font 3 centimes. Une économie de moins d'un centime
    // ne se propose pas, même si le seuil est dépassé : 2 centimes sur 3 mois font 1 centime de moyenne.
    [Fact]
    public void OverrunSavingsUseTheRoundedAverage()
    {
        Assert.Equal(2, SuggestionRules.FindCategoryOverrunSavings(-5, -10));
        Assert.Null(SuggestionRules.FindCategoryOverrunSavings(-1, -2));
    }

    [Fact]
    public void HugeOverrunStopsAtTheMaximum()
    {
        Assert.Equal(1_000_000_000, SuggestionRules.FindCategoryOverrunSavings(-50_000_000_000, -3));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-500, null)]
    [InlineData(1, 1)]
    [InlineData(70_000, 70_000)]
    [InlineData(5_000_000_000, 1_000_000_000)]
    public void UnallocatedMoneyIsAllTheAvailableMoney(long availableInCents, int? expectedSavings)
    {
        Assert.Equal(expectedSavings, SuggestionRules.FindUnallocatedMoneySavings(availableInCents));
    }

    // 2 000 € de revenus : l'épargne doit atteindre 400 €. À 400 € pile, rien ; un centime de moins, il manque un centime.
    // Une dépense plus grande que les revenus donne une épargne négative : il manque alors aussi ce qui a été dépensé en trop.
    [Theory]
    [InlineData(200_000, 160_000, null)]
    [InlineData(200_000, 160_001, 1)]
    [InlineData(200_000, 170_000, 10_000)]
    [InlineData(200_000, 250_000, 90_000)]
    public void MissingSavingsReachExactlyTwentyPercent(long incomeInCents, long expenseInCents, int? expectedSavings)
    {
        Assert.Equal(expectedSavings, SuggestionRules.FindMissingSavings(incomeInCents, expenseInCents));
    }

    // Le centime manquant est arrondi vers le haut : 3 centimes de revenus et 3 de dépenses demandent 0,6 centime d'épargne.
    [Fact]
    public void MissingSavingsAreRoundedUp()
    {
        Assert.Equal(1, SuggestionRules.FindMissingSavings(3, 3));
        Assert.Equal(1, SuggestionRules.FindMissingSavings(1, 1));
    }

    // Un mois sans revenus ne déclenche jamais la règle : 20 % de rien ne veut rien dire.
    [Theory]
    [InlineData(0, 50_000)]
    [InlineData(0, 0)]
    public void MonthWithoutIncomeNeverMissesSavings(long incomeInCents, long expenseInCents)
    {
        Assert.Null(SuggestionRules.FindMissingSavings(incomeInCents, expenseInCents));
    }

    [Fact]
    public void HugeMissingSavingsStopAtTheMaximum()
    {
        Assert.Equal(1_000_000_000, SuggestionRules.FindMissingSavings(1, 50_000_000_000));
    }

    // La pause dure 30 jours exactement après la réponse.
    [Fact]
    public void IgnoredSuggestionPausesItsRuleForThirtyDays()
    {
        var ignoredAt = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

        Assert.True(SuggestionRules.IsPaused(ignoredAt, ignoredAt));
        Assert.True(SuggestionRules.IsPaused(ignoredAt, ignoredAt.AddDays(30).AddTicks(-1)));
        Assert.False(SuggestionRules.IsPaused(ignoredAt, ignoredAt.AddDays(30)));
        Assert.Equal(TimeSpan.FromDays(30), SuggestionRules.IgnoredPause);
    }
}
