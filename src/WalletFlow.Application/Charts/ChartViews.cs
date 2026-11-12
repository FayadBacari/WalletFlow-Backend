namespace WalletFlow.Application.Charts;

// La dépense d'une catégorie sur une période, en centimes : le graphique de répartition en a une par catégorie dépensière.
public record CategoryExpense(Guid CategoryId, string CategoryName, long ExpenseInCents);

// Les revenus, les dépenses et l'épargne d'un mois, en centimes. L'épargne vaut les revenus moins les dépenses.
public record MonthTotals(int Year, int Month, long IncomeInCents, long ExpensesInCents, long SavingsInCents);

// Une catégorie dans la comparaison du mois en cours : son total net du mois, sa moyenne nette des 3 mois complets d'avant
// (null sans 3 mois complets d'historique), son budget et la part déjà dépensée, en pour cent (null sans budget).
// Un total net négatif veut dire que l'argent est surtout sorti.
public record CategoryComparison(
    Guid CategoryId,
    string CategoryName,
    bool IsOtherCategory,
    long CurrentMonthNetInCents,
    long? ThreeMonthAverageNetInCents,
    int? MonthlyBudgetInCents,
    int? BudgetUsedPercent);

// Le mois en cours face à la moyenne des 3 mois complets d'avant, catégorie par catégorie (GR Dépenses).
// Le mois en cours est montré tel qu'il est : DaysElapsed dit à quel jour du mois on le regarde.
public record MonthComparison(
    int Year,
    int Month,
    int DaysElapsed,
    int DaysInMonth,
    bool HasEnoughHistory,
    List<CategoryComparison> Categories);
