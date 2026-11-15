using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Tests.Domain;

// Vérifie quand partent les alertes qu'on peut désactiver, et combien de temps elles sont gardées (GR Dépenses, GR Sacs,
// GR Comptes, DD §9.3, §11 et §12).
public class AlertRulesTests
{
    // Le taux est arrondi vers le bas (DD §11) : un centime de moins que 80 % ne déclenche rien, 80 % tout juste déclenche
    // le premier seuil, et seul le budget entier déclenche le second. Le budget est de 1 000 €.
    [Theory]
    [InlineData(-79_999, null)]
    [InlineData(-80_000, (short)80)]
    [InlineData(-99_999, (short)80)]
    [InlineData(-100_000, (short)100)]
    [InlineData(-250_000, (short)100)]
    public void BudgetThresholdIsReachedOnlyOnceTheMoneyIsReallySpent(long monthNetInCents, short? expectedThreshold)
    {
        Assert.Equal(expectedThreshold, AlertRules.FindReachedBudgetThreshold(monthNetInCents, 100_000));
    }

    // Un total net positif est un revenu, pas une dépense : il n'atteint aucun seuil, même avec un budget d'un centime.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(500_000)]
    public void IncomeReachesNoThreshold(long monthNetInCents)
    {
        Assert.Null(AlertRules.FindReachedBudgetThreshold(monthNetInCents, 1));
    }

    // Les plus grands montants permis se comparent sans dépasser un long : 1 000 000 000 centimes de budget, et une dépense
    // mille fois plus grande.
    [Fact]
    public void LargestAmountsAreComparedWithoutOverflow()
    {
        Assert.Equal((short)100, AlertRules.FindReachedBudgetThreshold(-1_000_000_000_000, 1_000_000_000));
        Assert.Equal((short)80, AlertRules.FindReachedBudgetThreshold(-800_000_000, 1_000_000_000));
        Assert.Null(AlertRules.FindReachedBudgetThreshold(-799_999_999, 1_000_000_000));
    }

    // Le mois des dépenses est celui du jour donné, noté par son premier jour.
    [Theory]
    [InlineData(2026, 11, 1, 2026, 11)]
    [InlineData(2026, 11, 30, 2026, 11)]
    [InlineData(2027, 1, 15, 2027, 1)]
    public void BudgetMonthIsTheMonthOfTheDay(int year, int month, int day, int expectedYear, int expectedMonth)
    {
        Assert.Equal(new DateOnly(expectedYear, expectedMonth, 1), AlertRules.FindBudgetMonth(new DateOnly(year, month, day)));
    }

    // L'alerte d'un mois est gardée pendant les 2 mois qui suivent : en novembre, septembre est le premier mois gardé,
    // et en janvier, c'est novembre de l'année d'avant.
    [Theory]
    [InlineData(2026, 11, 4, 2026, 9)]
    [InlineData(2027, 1, 1, 2026, 11)]
    [InlineData(2027, 2, 28, 2026, 12)]
    public void AlertIsKeptForTheTwoMonthsAfterItsMonth(int year, int month, int day, int expectedYear, int expectedMonth)
    {
        Assert.Equal(new DateOnly(expectedYear, expectedMonth, 1), AlertRules.FindFirstKeptMonth(new DateOnly(year, month, day)));
    }

    // Un sac atteint son objectif dès que son solde l'égale. Sans objectif, il n'atteint rien.
    [Theory]
    [InlineData(99_999, 100_000, false)]
    [InlineData(100_000, 100_000, true)]
    [InlineData(150_000, 100_000, true)]
    [InlineData(150_000, null, false)]
    public void BagGoalIsReachedWhenTheBalanceEqualsIt(long balanceInCents, int? targetInCents, bool expectedReached)
    {
        Assert.Equal(expectedReached, AlertRules.IsBagGoalReached(balanceInCents, targetInCents));
    }

    // L'utilisateur est prévenu à partir de 7 jours avant la fin, et après la fin s'il ne l'a pas été.
    [Theory]
    [InlineData(7 * 24 * 60 * 60 + 1, false)]
    [InlineData(7 * 24 * 60 * 60, true)]
    [InlineData(60, true)]
    [InlineData(-30 * 24 * 60 * 60, true)]
    public void AuthorizationIsToRenewFromSevenDaysBeforeItsEnd(int secondsBeforeEnd, bool expectedToRenew)
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expectedToRenew, AlertRules.IsAuthorizationToRenew(now.AddSeconds(secondsBeforeEnd), now));
    }
}
