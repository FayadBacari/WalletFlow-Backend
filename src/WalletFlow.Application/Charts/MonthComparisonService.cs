using WalletFlow.Application.Categories;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Charts;

// Cas d'utilisation : l'utilisateur connecté compare le mois en cours avec la moyenne des 3 derniers mois, catégorie par
// catégorie, pour tous ses comptes ou pour un seul (GR Dépenses, DD §11). Le mois en cours est montré tel qu'il est ;
// la moyenne porte sur les 3 mois complets d'avant, divisés par 3, et n'existe qu'avec 3 mois complets d'historique.
// Chaque catégorie vient avec son budget et la part déjà dépensée (DD §11) ; les alertes viendront avec les notifications.
public class MonthComparisonService
{
    private readonly ITransactionRepository transactionRepository;
    private readonly ICategoryRepository categoryRepository;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux opérations et aux catégories, et l'horloge.
    public MonthComparisonService(ITransactionRepository transactionRepository, ICategoryRepository categoryRepository, TimeProvider clock)
    {
        this.transactionRepository = transactionRepository;
        this.categoryRepository = categoryRepository;
        this.clock = clock;
    }

    // Donne chaque catégorie de l'utilisateur, dans l'ordre de leur liste, avec son mois en cours et sa moyenne.
    public async Task<MonthComparison> ReadAsync(Guid userId, Guid? accountId)
    {
        var today = ChartRules.FindUsersToday(clock.GetUtcNow());
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var averageStart = monthStart.AddMonths(-ChartRules.AverageMonthCount);
        var totals = await transactionRepository.SumByCategoryAndMonthAsync(userId, averageStart, monthEnd, accountId);
        var earliestDate = await transactionRepository.FindEarliestDateAsync(userId, accountId);
        var hasEnoughHistory = earliestDate != null && earliestDate <= averageStart;
        var categories = await categoryRepository.FindAllAsync(userId);
        categories.Sort(CategoryOrder.Compare);
        var comparisons = new List<CategoryComparison>();

        foreach (var category in categories)
        {
            long currentMonthNetInCents = 0;
            long previousMonthsNetInCents = 0;

            foreach (var total in totals.Where(total => total.CategoryId == category.Id))
            {
                if (total.Year == today.Year && total.Month == today.Month)
                {
                    currentMonthNetInCents = currentMonthNetInCents + total.NetInCents;
                }
                else
                {
                    previousMonthsNetInCents = previousMonthsNetInCents + total.NetInCents;
                }
            }

            long? averageNetInCents = null;

            if (hasEnoughHistory)
            {
                averageNetInCents = ChartRules.AverageOf(previousMonthsNetInCents, ChartRules.AverageMonthCount);
            }

            comparisons.Add(new CategoryComparison(
                category.Id,
                category.Name,
                category.IsOtherCategory,
                currentMonthNetInCents,
                averageNetInCents,
                category.MonthlyBudgetInCents,
                ChartRules.CountBudgetUsedPercent(ChartRules.ExpenseOf(currentMonthNetInCents), category.MonthlyBudgetInCents)));
        }

        return new MonthComparison(
            today.Year,
            today.Month,
            today.Day,
            DateTime.DaysInMonth(today.Year, today.Month),
            hasEnoughHistory,
            comparisons);
    }
}
