using WalletFlow.Application.Categories;

namespace WalletFlow.Tests.Application;

// Vérifie le budget mensuel d'une catégorie (GR Dépenses, SS §4 et §5, DD §6.1), avec une fausse base.
public class CategoryBudgetServiceTests
{
    private readonly CategoryServices services = new CategoryServices();

    // Fixe le budget de cette catégorie pour l'utilisateur des tests.
    private async Task<CategoryBudgetResult> ReplaceAsync(Guid categoryId, int? budgetInCents)
    {
        return await services.CreateBudget().ReplaceAsync(services.UserId, categoryId, budgetInCents);
    }

    [Fact]
    public async Task BudgetIsSetThenRemoved()
    {
        var category = services.Find("Loisirs");

        var set = await ReplaceAsync(category.Id, 3000);
        Assert.Equal(3000, category.MonthlyBudgetInCents);

        var removed = await ReplaceAsync(category.Id, null);
        Assert.Null(category.MonthlyBudgetInCents);

        Assert.IsType<CategoryBudgetResult.Changed>(set);
        Assert.IsType<CategoryBudgetResult.Changed>(removed);
        Assert.Equal(2, services.UnitOfWork.CommittedTransactionCount);
    }

    // GR Dépenses : un budget par catégorie, « Autres » comprise.
    [Fact]
    public async Task OtherCategoryCanHaveABudget()
    {
        var otherCategory = services.Find("Autres");

        var result = await ReplaceAsync(otherCategory.Id, 1);

        Assert.IsType<CategoryBudgetResult.Changed>(result);
        Assert.Equal(1, otherCategory.MonthlyBudgetInCents);
    }

    [Fact]
    public async Task HighestBudgetIsAccepted()
    {
        var category = services.Find("Logement");

        var result = await ReplaceAsync(category.Id, 1_000_000_000);

        Assert.IsType<CategoryBudgetResult.Changed>(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_000_001)]
    [InlineData(int.MinValue)]
    public async Task BudgetOutsideTheLimitsIsRefusedBeforeReadingAnything(int budgetInCents)
    {
        var category = services.Find("Loisirs");

        var result = await ReplaceAsync(category.Id, budgetInCents);

        Assert.IsType<CategoryBudgetResult.RefusedByRules>(result);
        Assert.Equal(0, services.Repository.SearchCount);
        Assert.Null(category.MonthlyBudgetInCents);
    }

    // SS §4 : la catégorie d'un autre utilisateur n'est pas trouvée, et son budget ne change pas.
    [Fact]
    public async Task CategoryOfAnotherUserIsNotFound()
    {
        var otherUserCategory = services.AddOtherUserCategories("Loisirs");

        var result = await ReplaceAsync(otherUserCategory.Id, 3000);

        Assert.IsType<CategoryBudgetResult.NotFound>(result);
        Assert.Null(otherUserCategory.MonthlyBudgetInCents);
    }

    [Fact]
    public async Task EndlessConflictsKeepTheOldBudget()
    {
        var category = services.Find("Loisirs");
        await ReplaceAsync(category.Id, 3000);
        services.Repository.Conflicts = 10;

        var result = await ReplaceAsync(category.Id, 9000);

        Assert.IsType<CategoryBudgetResult.NotSaved>(result);
        Assert.Equal(3000, category.MonthlyBudgetInCents);
    }
}
