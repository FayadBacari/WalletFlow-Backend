using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Charts;

// Cas d'utilisation : l'utilisateur connecté voit l'évolution de ses dépenses et de ses revenus mois par mois,
// pour tous ses comptes ou pour un seul (GR Dépenses). Dans chaque mois, chaque catégorie compte pour son total net :
// négatif, c'est une dépense ; positif, un revenu (voir ChartRules). L'épargne du mois est la différence des deux.
public class MonthlyTotalsChartService
{
    private readonly ITransactionRepository transactionRepository;

    // Reçoit l'accès aux opérations.
    public MonthlyTotalsChartService(ITransactionRepository transactionRepository)
    {
        this.transactionRepository = transactionRepository;
    }

    // Donne les totaux de chaque mois civil que touche la période, dans l'ordre. Un mois sans opération vaut zéro :
    // les barres ne sautent aucun mois. Le premier et le dernier mois ne comptent que les jours de la période.
    public async Task<ChartPeriodResult<List<MonthTotals>>> ReadAsync(Guid userId, DateOnly from, DateOnly to, Guid? accountId)
    {
        if (to < from)
        {
            return new ChartPeriodResult<List<MonthTotals>>.DatesReversed();
        }

        if (ChartRules.IsPeriodTooLong(from, to))
        {
            return new ChartPeriodResult<List<MonthTotals>>.PeriodTooLong();
        }

        var totals = await transactionRepository.SumByCategoryAndMonthAsync(userId, from, to, accountId);
        var firstMonth = new DateOnly(from.Year, from.Month, 1);
        var months = new List<MonthTotals>();

        // Chaque mois part du premier : aucun calcul ne dépasse le dernier mois de la période.
        for (var monthNumber = 0; monthNumber < ChartRules.CountMonths(from, to); monthNumber++)
        {
            var month = firstMonth.AddMonths(monthNumber);
            long incomeInCents = 0;
            long expensesInCents = 0;

            foreach (var total in totals.Where(total => total.Year == month.Year && total.Month == month.Month))
            {
                incomeInCents = incomeInCents + ChartRules.IncomeOf(total.NetInCents);
                expensesInCents = expensesInCents + ChartRules.ExpenseOf(total.NetInCents);
            }

            months.Add(new MonthTotals(month.Year, month.Month, incomeInCents, expensesInCents, incomeInCents - expensesInCents));
        }

        return new ChartPeriodResult<List<MonthTotals>>.Found { Chart = months };
    }
}
