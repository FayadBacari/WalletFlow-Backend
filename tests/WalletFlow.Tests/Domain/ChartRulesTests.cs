using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles des graphiques (GR Dépenses, DD §11).
public class ChartRulesTests
{
    [Fact]
    public void LimitsAreTheChosenOnes()
    {
        Assert.Equal("Europe/Paris", ChartRules.UsersTimeZoneId);
        Assert.Equal(120, ChartRules.MaximumPeriodMonths);
        Assert.Equal(3, ChartRules.AverageMonthCount);
    }

    // Le mois en cours commence à minuit, heure de Paris : une heure ou deux avant minuit en temps universel.
    [Theory]
    [InlineData("2026-11-30T22:59:00Z", "2026-11-30")]
    [InlineData("2026-11-30T23:00:00Z", "2026-12-01")]
    [InlineData("2026-06-30T21:59:00Z", "2026-06-30")]
    [InlineData("2026-06-30T22:00:00Z", "2026-07-01")]
    public void TodayIsTheDayInParis(string utcNow, string expectedDay)
    {
        Assert.Equal(DateOnly.Parse(expectedDay), ChartRules.FindUsersToday(DateTimeOffset.Parse(utcNow)));
    }

    // Une période compte chaque mois civil qu'elle touche, même pour un seul jour.
    [Theory]
    [InlineData("2026-11-01", "2026-11-30", 1)]
    [InlineData("2026-11-30", "2026-12-01", 2)]
    [InlineData("2026-01-15", "2026-12-15", 12)]
    [InlineData("2017-01-01", "2026-12-31", 120)]
    [InlineData("2016-12-31", "2026-12-31", 121)]
    public void PeriodCountsEveryMonthItTouches(string from, string to, int expectedMonths)
    {
        Assert.Equal(expectedMonths, ChartRules.CountMonths(DateOnly.Parse(from), DateOnly.Parse(to)));
        Assert.Equal(expectedMonths > 120, ChartRules.IsPeriodTooLong(DateOnly.Parse(from), DateOnly.Parse(to)));
    }

    // Un total net négatif est une dépense de ce montant, un total positif un revenu : jamais les deux à la fois.
    [Theory]
    [InlineData(-4520, 4520, 0)]
    [InlineData(250_000, 0, 250_000)]
    [InlineData(0, 0, 0)]
    public void NetIsEitherAnExpenseOrAnIncome(long netInCents, long expectedExpense, long expectedIncome)
    {
        Assert.Equal(expectedExpense, ChartRules.ExpenseOf(netInCents));
        Assert.Equal(expectedIncome, ChartRules.IncomeOf(netInCents));
    }

    // Une moyenne est arrondie au centime le plus proche ; un demi-centime s'éloigne de zéro, dans les deux sens.
    [Theory]
    [InlineData(-15_000, 3, -5000)]
    [InlineData(100, 3, 33)]
    [InlineData(200, 3, 67)]
    [InlineData(-200, 3, -67)]
    [InlineData(3, 2, 2)]
    [InlineData(5, 2, 3)]
    [InlineData(-3, 2, -2)]
    [InlineData(-5, 2, -3)]
    public void AverageIsRoundedToTheNearestCent(long totalInCents, int monthCount, long expectedAverage)
    {
        Assert.Equal(expectedAverage, ChartRules.AverageOf(totalInCents, monthCount));
    }

    // DD §11 : le taux est arrondi vers le bas, pour que 80 % ne soit atteint qu'une fois 80 % vraiment dépensés.
    [Theory]
    [InlineData(7999, 10_000, 79)]
    [InlineData(8000, 10_000, 80)]
    [InlineData(0, 10_000, 0)]
    [InlineData(25_000, 10_000, 250)]
    [InlineData(1_000_000_000, 1, 2_147_483_647)]
    public void BudgetUsageIsRoundedDown(long spentInCents, int budgetInCents, int expectedPercent)
    {
        Assert.Equal<int?>(expectedPercent, ChartRules.CountBudgetUsedPercent(spentInCents, budgetInCents));
    }

    [Fact]
    public void CategoryWithoutBudgetHasNoUsage()
    {
        Assert.Null(ChartRules.CountBudgetUsedPercent(5000, null));
    }
}
