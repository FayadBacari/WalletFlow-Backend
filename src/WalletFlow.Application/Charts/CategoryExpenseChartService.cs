using WalletFlow.Application.Categories;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Charts;

// Cas d'utilisation : l'utilisateur connecté voit la répartition de ses dépenses par catégorie sur une période,
// pour tous ses comptes ou pour un seul (GR Dépenses). Chaque catégorie compte pour son total net sur la période :
// seules celles dont l'argent est surtout sorti sont des dépenses (voir ChartRules).
public class CategoryExpenseChartService
{
    private readonly ITransactionRepository transactionRepository;
    private readonly ICategoryRepository categoryRepository;

    // Reçoit l'accès aux opérations et aux catégories.
    public CategoryExpenseChartService(ITransactionRepository transactionRepository, ICategoryRepository categoryRepository)
    {
        this.transactionRepository = transactionRepository;
        this.categoryRepository = categoryRepository;
    }

    // Donne la dépense de chaque catégorie dépensière entre ces deux jours compris, la plus grande d'abord.
    // À dépense égale, les catégories gardent l'ordre de leur liste (voir CategoryOrder).
    public async Task<ChartPeriodResult<List<CategoryExpense>>> ReadAsync(Guid userId, DateOnly from, DateOnly to, Guid? accountId)
    {
        if (to < from)
        {
            return new ChartPeriodResult<List<CategoryExpense>>.DatesReversed();
        }

        if (ChartRules.IsPeriodTooLong(from, to))
        {
            return new ChartPeriodResult<List<CategoryExpense>>.PeriodTooLong();
        }

        var totals = await transactionRepository.SumByCategoryAndMonthAsync(userId, from, to, accountId);
        var categories = await categoryRepository.FindAllAsync(userId);
        categories.Sort(CategoryOrder.Compare);
        var expenses = new List<CategoryExpense>();

        foreach (var category in categories)
        {
            var netInCents = totals.Where(total => total.CategoryId == category.Id).Sum(total => total.NetInCents);
            var expenseInCents = ChartRules.ExpenseOf(netInCents);

            if (expenseInCents > 0)
            {
                expenses.Add(new CategoryExpense(category.Id, category.Name, expenseInCents));
            }
        }

        return new ChartPeriodResult<List<CategoryExpense>>.Found
        {
            Chart = expenses.OrderByDescending(expense => expense.ExpenseInCents).ToList()
        };
    }
}
