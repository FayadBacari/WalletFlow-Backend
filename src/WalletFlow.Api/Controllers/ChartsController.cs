using Microsoft.AspNetCore.Mvc;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Charts;

namespace WalletFlow.Api.Controllers;

// Les chiffres des graphiques de l'utilisateur connecté (GR Dépenses) : la répartition des dépenses par catégorie,
// les dépenses et les revenus mois par mois, et le mois en cours face à la moyenne des 3 derniers mois. Le site les dessine.
// Ces routes n'ont aucun attribut de sécurité : toute route demande par défaut une session (voir Program.cs).
// Le compte d'un autre utilisateur ne filtre rien de visible : aucune de ses opérations ne compte (SS §4).
[ApiController]
[Route("api/charts")]
public class ChartsController : ControllerBase
{
    private readonly CategoryExpenseChartService categoryExpenseService;
    private readonly MonthlyTotalsChartService monthlyTotalsService;
    private readonly MonthComparisonService comparisonService;

    // Reçoit les cas d'utilisation des graphiques.
    public ChartsController(
        CategoryExpenseChartService categoryExpenseService,
        MonthlyTotalsChartService monthlyTotalsService,
        MonthComparisonService comparisonService)
    {
        this.categoryExpenseService = categoryExpenseService;
        this.monthlyTotalsService = monthlyTotalsService;
        this.comparisonService = comparisonService;
    }

    // GET api/charts/categories : la répartition des dépenses par catégorie entre deux dates comprises, la plus grande d'abord.
    // Ce sont des données financières : aucun navigateur ni intermédiaire ne doit les garder.
    [HttpGet("categories")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ReadCategoryExpenses([FromQuery] ChartPeriodRequest request)
    {
        if (request.From is not DateOnly from || request.To is not DateOnly to)
        {
            return AnswerPeriodMissing(request);
        }

        var result = await categoryExpenseService.ReadAsync(SessionClaims.ReadUserId(User), from, to, request.AccountId);

        if (result is ChartPeriodResult<List<CategoryExpense>>.Found found)
        {
            var categories = found.Chart
                .Select(expense => new CategoryExpenseResponse(expense.CategoryId, expense.CategoryName, expense.ExpenseInCents))
                .ToList();

            return Ok(new CategoryExpenseChartResponse(from, to, categories.Sum(category => category.ExpenseInCents), categories));
        }

        return AnswerPeriodRefused(result is ChartPeriodResult<List<CategoryExpense>>.DatesReversed);
    }

    // GET api/charts/months : les revenus, les dépenses et l'épargne de chaque mois que touche la période, dans l'ordre.
    [HttpGet("months")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ReadMonthlyTotals([FromQuery] ChartPeriodRequest request)
    {
        if (request.From is not DateOnly from || request.To is not DateOnly to)
        {
            return AnswerPeriodMissing(request);
        }

        var result = await monthlyTotalsService.ReadAsync(SessionClaims.ReadUserId(User), from, to, request.AccountId);

        if (result is ChartPeriodResult<List<MonthTotals>>.Found found)
        {
            var months = found.Chart
                .Select(month => new MonthTotalsResponse(month.Year, month.Month, month.IncomeInCents, month.ExpensesInCents, month.SavingsInCents))
                .ToList();

            return Ok(new MonthlyTotalsChartResponse(from, to, months));
        }

        return AnswerPeriodRefused(result is ChartPeriodResult<List<MonthTotals>>.DatesReversed);
    }

    // GET api/charts/comparison : le mois en cours, tel qu'il est, face à la moyenne des 3 mois complets d'avant,
    // catégorie par catégorie, avec le budget de chaque catégorie et la part déjà dépensée.
    [HttpGet("comparison")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ReadComparison([FromQuery] ChartAccountRequest request)
    {
        var comparison = await comparisonService.ReadAsync(SessionClaims.ReadUserId(User), request.AccountId);
        var categories = comparison.Categories
            .Select(category => new CategoryComparisonResponse(
                category.CategoryId,
                category.CategoryName,
                category.IsOtherCategory,
                category.CurrentMonthNetInCents,
                category.ThreeMonthAverageNetInCents,
                category.MonthlyBudgetInCents,
                category.BudgetUsedPercent))
            .ToList();

        return Ok(new MonthComparisonResponse(
            comparison.Year,
            comparison.Month,
            comparison.DaysElapsed,
            comparison.DaysInMonth,
            comparison.HasEnoughHistory,
            categories));
    }

    // Refuse une période sans date de début ou sans date de fin, sur le champ qui manque.
    private ActionResult AnswerPeriodMissing(ChartPeriodRequest request)
    {
        if (request.From == null)
        {
            ModelState.AddModelError(nameof(ChartPeriodRequest.From), ErrorMessages.ChartPeriodRequired);
        }

        if (request.To == null)
        {
            ModelState.AddModelError(nameof(ChartPeriodRequest.To), ErrorMessages.ChartPeriodRequired);
        }

        return ValidationProblem();
    }

    // Refuse une période à l'envers, ou trop longue, sur le champ To.
    private ActionResult AnswerPeriodRefused(bool areDatesReversed)
    {
        var message = areDatesReversed ? ErrorMessages.DatesReversed : ErrorMessages.ChartPeriodTooLong;
        ModelState.AddModelError(nameof(ChartPeriodRequest.To), message);

        return ValidationProblem();
    }
}
