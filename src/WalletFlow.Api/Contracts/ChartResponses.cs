namespace WalletFlow.Api.Contracts;

// La dépense d'une catégorie sur la période, en centimes, toujours positive.
public record CategoryExpenseResponse(Guid CategoryId, string CategoryName, long ExpenseInCents);

// La répartition des dépenses par catégorie sur la période (GR Dépenses), la plus grande d'abord, et leur total.
public record CategoryExpenseChartResponse(DateOnly From, DateOnly To, long TotalExpensesInCents, List<CategoryExpenseResponse> Categories);

// Les revenus, les dépenses et l'épargne d'un mois, en centimes. L'épargne vaut les revenus moins les dépenses.
public record MonthTotalsResponse(int Year, int Month, long IncomeInCents, long ExpensesInCents, long SavingsInCents);

// L'évolution des dépenses et des revenus mois par mois sur la période (GR Dépenses), un mois sans opération valant zéro.
public record MonthlyTotalsChartResponse(DateOnly From, DateOnly To, List<MonthTotalsResponse> Months);

// Une catégorie dans la comparaison du mois en cours. Les totaux sont nets, en centimes : négatifs quand l'argent est surtout
// sorti. La moyenne est null sans 3 mois complets d'historique ; la part du budget dépensée, en pour cent, est null sans budget.
public record CategoryComparisonResponse(
    Guid CategoryId,
    string CategoryName,
    bool IsOtherCategory,
    long CurrentMonthNetInCents,
    long? ThreeMonthAverageNetInCents,
    int? MonthlyBudgetInCents,
    int? BudgetUsedPercent);

// Le mois en cours face à la moyenne des 3 mois complets d'avant (GR Dépenses), au jour DaysElapsed du mois.
public record MonthComparisonResponse(
    int Year,
    int Month,
    int DaysElapsed,
    int DaysInMonth,
    bool HasEnoughHistory,
    List<CategoryComparisonResponse> Categories);
