using WalletFlow.Application.Charts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie les chiffres des graphiques (GR Dépenses, GR Comptes, SS §4, DD §11), avec une fausse base.
// L'horloge des tests marque le 4 novembre 2026 à midi : le mois en cours est novembre 2026.
public class ChartServicesTests
{
    private static readonly DateOnly November1 = new DateOnly(2026, 11, 1);
    private static readonly DateOnly November30 = new DateOnly(2026, 11, 30);

    private readonly BankServices services = new BankServices();

    // Connecte un compte courant, et le renvoie.
    private async Task<BankAccount> ConnectAsync(string stableId = "stable-1")
    {
        var connection = await services.ConnectAsync(new TestBankAccount(stableId, "Compte " + stableId, "EUR", 10_000));

        return services.AccountsOf(connection).Single();
    }

    // Donne la répartition des dépenses de l'utilisateur des tests sur la période.
    private async Task<List<CategoryExpense>> ReadExpensesAsync(DateOnly from, DateOnly to, Guid? accountId = null)
    {
        var result = await services.CreateCategoryExpenseChart().ReadAsync(services.UserId, from, to, accountId);

        return Assert.IsType<ChartPeriodResult<List<CategoryExpense>>.Found>(result).Chart;
    }

    // Donne les totaux mois par mois de l'utilisateur des tests sur la période.
    private async Task<List<MonthTotals>> ReadMonthsAsync(DateOnly from, DateOnly to, Guid? accountId = null)
    {
        var result = await services.CreateMonthlyTotalsChart().ReadAsync(services.UserId, from, to, accountId);

        return Assert.IsType<ChartPeriodResult<List<MonthTotals>>.Found>(result).Chart;
    }

    // Donne la ligne de cette catégorie dans la comparaison.
    private static CategoryComparison LineOf(MonthComparison comparison, string categoryName)
    {
        return comparison.Categories.Single(line => line.CategoryName == categoryName);
    }

    // --- Répartition par catégorie

    // Décision 1A : un remboursement réduit les dépenses de sa catégorie. Une catégorie dont l'argent est surtout entré
    // n'est pas une dépense : « Revenus », ou « Santé » le mois où la CPAM rembourse plus que les soins.
    [Fact]
    public async Task EachCategoryCountsForItsNet()
    {
        var account = await ConnectAsync();
        services.SaveTransaction(account, "Alimentation", November1, -4520);
        services.SaveTransaction(account, "Alimentation", November1.AddDays(2), 1250);
        services.SaveTransaction(account, "Revenus", November1.AddDays(3), 250_000);
        services.SaveTransaction(account, "Santé", November1.AddDays(1), -2000);
        services.SaveTransaction(account, "Santé", November1.AddDays(5), 2500);
        services.SaveTransaction(account, "Transport", November1.AddDays(4), -1000);

        var expenses = await ReadExpensesAsync(November1, November30);

        Assert.Equal(
            new List<CategoryExpense>
            {
                new CategoryExpense(services.CategoryNamed("Alimentation").Id, "Alimentation", 3270),
                new CategoryExpense(services.CategoryNamed("Transport").Id, "Transport", 1000)
            },
            expenses);
    }

    // GR Comptes : un transfert ne compte ni en dépense ni en revenu.
    [Fact]
    public async Task TransfersNeverCount()
    {
        var account = await ConnectAsync();
        services.SaveTransaction(account, "Autres", November1, -50_000, isInternalTransfer: true);
        services.SaveTransaction(account, "Revenus", November1, 50_000, isInternalTransfer: true);

        Assert.Empty(await ReadExpensesAsync(November1, November30));
        Assert.Equal(new MonthTotals(2026, 11, 0, 0, 0), Assert.Single(await ReadMonthsAsync(November1, November30)));
    }

    // Les deux dates de la période sont comprises ; la veille et le lendemain ne comptent pas.
    [Fact]
    public async Task PeriodIncludesBothDates()
    {
        var account = await ConnectAsync();
        services.SaveTransaction(account, "Loisirs", November1.AddDays(-1), -100);
        services.SaveTransaction(account, "Loisirs", November1, -200);
        services.SaveTransaction(account, "Loisirs", November1.AddDays(9), -400);
        services.SaveTransaction(account, "Loisirs", November1.AddDays(10), -800);

        Assert.Equal(600, Assert.Single(await ReadExpensesAsync(November1, November1.AddDays(9))).ExpenseInCents);
    }

    // À dépense égale, les catégories gardent l'ordre de leur liste : alphabétique, « Autres » à la fin.
    [Fact]
    public async Task EqualExpensesKeepTheListOrder()
    {
        var account = await ConnectAsync();
        services.SaveTransaction(account, "Autres", November1, -500);
        services.SaveTransaction(account, "Logement", November1, -500);
        services.SaveTransaction(account, "Transport", November1, -500);
        services.SaveTransaction(account, "Alimentation", November1, -500);

        var names = (await ReadExpensesAsync(November1, November30)).Select(expense => expense.CategoryName).ToList();

        Assert.Equal(new List<string> { "Alimentation", "Logement", "Transport", "Autres" }, names);
    }

    // GR Dépenses : le filtre par compte garde ses seules opérations. SS §4 : le compte d'un autre utilisateur ne montre rien.
    [Fact]
    public async Task AccountFilterKeepsOnlyItsOperations()
    {
        var current = await ConnectAsync("stable-1");
        var savings = await ConnectAsync("stable-2");
        services.SaveTransaction(current, "Alimentation", November1, -1000);
        services.SaveTransaction(savings, "Alimentation", November1, -3000);

        Assert.Equal(4000, Assert.Single(await ReadExpensesAsync(November1, November30)).ExpenseInCents);
        Assert.Equal(1000, Assert.Single(await ReadExpensesAsync(November1, November30, current.Id)).ExpenseInCents);
        Assert.Empty(await ReadExpensesAsync(November1, November30, Guid.NewGuid()));

        var otherUser = await services.CreateCategoryExpenseChart().ReadAsync(Guid.NewGuid(), November1, November30, current.Id);
        Assert.Empty(Assert.IsType<ChartPeriodResult<List<CategoryExpense>>.Found>(otherUser).Chart);
    }

    // Une période à l'envers ou de plus de 10 ans est refusée ; 10 ans tout juste sont acceptés.
    [Fact]
    public async Task PeriodMustBeInOrderAndAtMostTenYears()
    {
        var expenses = services.CreateCategoryExpenseChart();
        var months = services.CreateMonthlyTotalsChart();
        var tenYearsStart = new DateOnly(2017, 1, 1);
        var tenYearsEnd = new DateOnly(2026, 12, 31);

        Assert.IsType<ChartPeriodResult<List<CategoryExpense>>.DatesReversed>(await expenses.ReadAsync(services.UserId, November30, November1, null));
        Assert.IsType<ChartPeriodResult<List<MonthTotals>>.DatesReversed>(await months.ReadAsync(services.UserId, November30, November1, null));
        Assert.IsType<ChartPeriodResult<List<CategoryExpense>>.PeriodTooLong>(await expenses.ReadAsync(services.UserId, tenYearsStart.AddDays(-1), tenYearsEnd, null));
        Assert.IsType<ChartPeriodResult<List<MonthTotals>>.PeriodTooLong>(await months.ReadAsync(services.UserId, tenYearsStart.AddDays(-1), tenYearsEnd, null));
        Assert.IsType<ChartPeriodResult<List<CategoryExpense>>.Found>(await expenses.ReadAsync(services.UserId, tenYearsStart, tenYearsEnd, null));
        Assert.Equal(120, (await ReadMonthsAsync(tenYearsStart, tenYearsEnd)).Count);
    }

    // --- Mois par mois

    // Décision 1A : dans chaque mois, chaque catégorie compte pour son total net. L'épargne du mois vaut exactement
    // l'argent entré moins l'argent sorti.
    [Fact]
    public async Task MonthTotalsUseTheNetOfEachCategory()
    {
        var account = await ConnectAsync();
        services.SaveTransaction(account, "Alimentation", November1, -4520);
        services.SaveTransaction(account, "Alimentation", November1.AddDays(2), 1250);
        services.SaveTransaction(account, "Revenus", November1.AddDays(3), 250_000);
        services.SaveTransaction(account, "Santé", November1.AddDays(1), -2000);
        services.SaveTransaction(account, "Santé", November1.AddDays(5), 2500);

        var month = Assert.Single(await ReadMonthsAsync(November1, November30));

        Assert.Equal(new MonthTotals(2026, 11, 250_500, 3270, 247_230), month);
        Assert.Equal(-4520 + 1250 + 250_000 - 2000 + 2500, month.SavingsInCents);
    }

    // Chaque mois que touche la période apparaît, dans l'ordre, même sans opération, d'une année à la suivante.
    // Le premier et le dernier mois ne comptent que les jours de la période.
    [Fact]
    public async Task EveryMonthOfThePeriodAppearsInOrder()
    {
        var account = await ConnectAsync();
        services.SaveTransaction(account, "Loisirs", new DateOnly(2025, 12, 10), -1000);
        services.SaveTransaction(account, "Loisirs", new DateOnly(2025, 12, 20), -2000);
        services.SaveTransaction(account, "Loisirs", new DateOnly(2026, 3, 5), -4000);
        services.SaveTransaction(account, "Loisirs", new DateOnly(2026, 3, 25), -8000);

        var months = await ReadMonthsAsync(new DateOnly(2025, 12, 15), new DateOnly(2026, 3, 10));

        Assert.Equal(
            new List<MonthTotals>
            {
                new MonthTotals(2025, 12, 0, 2000, -2000),
                new MonthTotals(2026, 1, 0, 0, 0),
                new MonthTotals(2026, 2, 0, 0, 0),
                new MonthTotals(2026, 3, 0, 4000, -4000)
            },
            months);
    }

    // --- Comparaison du mois en cours

    // Décisions 2A et 3A : le mois en cours tel qu'il est au 4 novembre, face à la moyenne d'août, septembre et octobre,
    // divisée par 3 : octobre, sans dépense, compte pour zéro.
    [Fact]
    public async Task CurrentMonthFacesTheAverageOfTheThreeCompleteMonthsBefore()
    {
        var account = await ConnectAsync();
        services.SaveTransaction(account, "Alimentation", new DateOnly(2026, 7, 31), -99_999);
        services.SaveTransaction(account, "Alimentation", new DateOnly(2026, 8, 1), -9000);
        services.SaveTransaction(account, "Alimentation", new DateOnly(2026, 9, 15), -6000);
        services.SaveTransaction(account, "Alimentation", new DateOnly(2026, 11, 2), -3000);

        var comparison = await services.CreateMonthComparison().ReadAsync(services.UserId, null);

        Assert.Equal((2026, 11, 4, 30, true), (comparison.Year, comparison.Month, comparison.DaysElapsed, comparison.DaysInMonth, comparison.HasEnoughHistory));
        var food = LineOf(comparison, "Alimentation");
        Assert.Equal(-3000, food.CurrentMonthNetInCents);
        Assert.Equal(-5000, food.ThreeMonthAverageNetInCents);
        Assert.Equal(0, LineOf(comparison, "Loisirs").ThreeMonthAverageNetInCents);
    }

    // Décision 2A : sans 3 mois complets d'historique, il n'y a pas de moyenne ; le mois en cours reste montré.
    [Fact]
    public async Task AverageNeedsThreeCompleteMonthsOfHistory()
    {
        var account = await ConnectAsync();
        services.SaveTransaction(account, "Alimentation", new DateOnly(2026, 8, 2), -9000);
        services.SaveTransaction(account, "Alimentation", new DateOnly(2026, 11, 2), -3000);

        var comparison = await services.CreateMonthComparison().ReadAsync(services.UserId, null);

        Assert.False(comparison.HasEnoughHistory);
        Assert.All(comparison.Categories, line => Assert.Null(line.ThreeMonthAverageNetInCents));
        Assert.Equal(-3000, LineOf(comparison, "Alimentation").CurrentMonthNetInCents);
    }

    // DD §11 : chaque catégorie vient avec son budget et la part dépensée ce mois-ci ; une catégorie sans budget n'en a pas.
    // Toutes les catégories sont là, dans l'ordre de leur liste, même sans opération.
    [Fact]
    public async Task EveryCategoryComesWithItsBudgetUsage()
    {
        var account = await ConnectAsync();
        services.CategoryNamed("Alimentation").ReplaceMonthlyBudget(10_000);
        services.CategoryNamed("Santé").ReplaceMonthlyBudget(5000);
        services.SaveTransaction(account, "Alimentation", November1, -8000);
        services.SaveTransaction(account, "Santé", November1, -2000);
        services.SaveTransaction(account, "Santé", November1.AddDays(1), 2500);

        var comparison = await services.CreateMonthComparison().ReadAsync(services.UserId, null);

        Assert.Equal(80, LineOf(comparison, "Alimentation").BudgetUsedPercent);
        Assert.Equal(0, LineOf(comparison, "Santé").BudgetUsedPercent);
        Assert.Null(LineOf(comparison, "Loisirs").BudgetUsedPercent);
        Assert.Equal(10_000, LineOf(comparison, "Alimentation").MonthlyBudgetInCents);
        Assert.Equal(
            new List<string> { "Alimentation", "Logement", "Loisirs", "Revenus", "Santé", "Shopping", "Transport", "Autres" },
            comparison.Categories.Select(line => line.CategoryName).ToList());
    }

    // Le mois en cours commence à minuit, heure de Paris : le 30 novembre à 23 h 30 en temps universel, c'est déjà décembre,
    // et novembre entre dans la moyenne.
    [Fact]
    public async Task CurrentMonthStartsAtMidnightInParis()
    {
        var account = await ConnectAsync();
        services.SaveTransaction(account, "Alimentation", new DateOnly(2026, 9, 1), -3000);
        services.SaveTransaction(account, "Alimentation", new DateOnly(2026, 11, 30), -6000);
        services.Clock.Advance(new DateTimeOffset(2026, 11, 30, 23, 30, 0, TimeSpan.Zero) - services.Clock.GetUtcNow());

        var comparison = await services.CreateMonthComparison().ReadAsync(services.UserId, null);

        Assert.Equal((2026, 12, 1), (comparison.Year, comparison.Month, comparison.DaysElapsed));
        Assert.True(comparison.HasEnoughHistory);
        Assert.Equal(0, LineOf(comparison, "Alimentation").CurrentMonthNetInCents);
        Assert.Equal(-3000, LineOf(comparison, "Alimentation").ThreeMonthAverageNetInCents);
    }

    // Avec un compte, la comparaison et son historique ne regardent que ce compte. Un transfert ne compte jamais.
    [Fact]
    public async Task ComparisonFollowsTheAccountFilterWithoutTransfers()
    {
        var current = await ConnectAsync("stable-1");
        var savings = await ConnectAsync("stable-2");
        services.SaveTransaction(current, "Alimentation", new DateOnly(2026, 6, 1), -3000);
        services.SaveTransaction(savings, "Alimentation", new DateOnly(2026, 10, 1), -6000);
        services.SaveTransaction(current, "Autres", November1, -50_000, isInternalTransfer: true);

        var currentComparison = await services.CreateMonthComparison().ReadAsync(services.UserId, current.Id);
        var savingsComparison = await services.CreateMonthComparison().ReadAsync(services.UserId, savings.Id);

        Assert.True(currentComparison.HasEnoughHistory);
        Assert.Equal(0, LineOf(currentComparison, "Autres").CurrentMonthNetInCents);
        Assert.False(savingsComparison.HasEnoughHistory);
    }
}
