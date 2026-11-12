using WalletFlow.Application.Categories;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Application;

// Vérifie la création d'une catégorie (GR Dépenses, SS §6, DD §6.1), avec une fausse base.
public class CategoryCreationServiceTests
{
    private readonly CategoryServices services = new CategoryServices();

    // Crée une catégorie avec ce nom, pour l'utilisateur des tests.
    private async Task<CategoryCreationResult> CreateAsync(string? typedName)
    {
        return await services.CreateCreation().CreateAsync(services.UserId, typedName);
    }

    [Fact]
    public async Task CategoryIsCreatedWithItsSavedNameAndNoBudget()
    {
        var result = await CreateAsync("  Abonnements ");

        var created = Assert.IsType<CategoryCreationResult.Created>(result);
        Assert.Equal("Abonnements", created.Category.Name);
        Assert.Null(created.Category.MonthlyBudgetInCents);
        Assert.False(created.Category.IsOtherCategory);
        Assert.Equal(services.UserId, created.Category.UserId);
        Assert.Contains(created.Category, services.Repository.SavedCategories);
        Assert.Equal(1, services.UnitOfWork.CommittedTransactionCount);
    }

    // « Autres » est réécrite avec la création : c'est elle qui empêche deux créations simultanées de dépasser 100.
    [Fact]
    public async Task OtherCategoryIsWrittenWithTheNewCategory()
    {
        await CreateAsync("Abonnements");

        var checkedCategory = Assert.Single(services.Repository.CheckedCategories);
        Assert.Same(services.Find("Autres"), checkedCategory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task MissingNameIsRefusedBeforeReadingAnything(string? typedName)
    {
        var result = await CreateAsync(typedName);

        Assert.IsType<CategoryCreationResult.RefusedByRules>(result);
        Assert.Equal(0, services.Repository.SearchCount);
        Assert.Equal(0, services.UnitOfWork.CommittedTransactionCount);
    }

    [Fact]
    public async Task TooLongOrInvisibleNameIsRefused()
    {
        Assert.IsType<CategoryCreationResult.RefusedByRules>(await CreateAsync(new string('a', 51)));
        Assert.IsType<CategoryCreationResult.RefusedByRules>(await CreateAsync("Abon" + (char)0x200B + "nements"));
        Assert.Equal(8, services.ListSavedNames().Count);
    }

    // DD §6.1 : deux catégories d'un même utilisateur ne portent pas le même nom, sans tenir compte des majuscules.
    [Theory]
    [InlineData("loisirs")]
    [InlineData("LOISIRS")]
    [InlineData(" Loisirs ")]
    [InlineData("autres")]
    public async Task NameAlreadyUsedIsRefusedWhateverItsCase(string typedName)
    {
        var result = await CreateAsync(typedName);

        Assert.IsType<CategoryCreationResult.NameAlreadyUsed>(result);
        Assert.Equal(8, services.ListSavedNames().Count);
    }

    // Le nom d'une catégorie d'un autre utilisateur ne compte pas.
    [Fact]
    public async Task NameOfAnotherUserCategoryIsAccepted()
    {
        var otherUserCategory = services.AddOtherUserCategories("Loisirs");
        services.Repository.SavedCategories.Add(new Category { Name = "Abonnements", UserId = otherUserCategory.UserId });

        var result = await CreateAsync("Abonnements");

        Assert.IsType<CategoryCreationResult.Created>(result);
    }

    // SS §6 : 100 catégories au plus, les 8 par défaut comprises.
    [Fact]
    public async Task HundredthCategoryIsCreatedButNotTheNextOne()
    {
        services.AddCategoriesUpTo(99);

        var hundredth = await CreateAsync("Abonnements");
        var next = await CreateAsync("Cadeaux");

        Assert.IsType<CategoryCreationResult.Created>(hundredth);
        Assert.IsType<CategoryCreationResult.TooManyCategories>(next);
        Assert.Equal(100, services.ListSavedNames().Count);
    }

    // Une autre requête crée le même nom juste avant l'enregistrement : le passage suivant voit que le nom est pris.
    [Fact]
    public async Task SameNameCreatedMeanwhileIsSeenOnTheNextTry()
    {
        services.Repository.CategoryCreatedBeforeSave = new Category { Name = "Abonnements", UserId = services.UserId };

        var result = await CreateAsync("abonnements");

        Assert.IsType<CategoryCreationResult.NameAlreadyUsed>(result);
        Assert.Single(services.ListSavedNames(), name => name.ToLowerInvariant() == "abonnements");
    }

    // Une autre requête crée la 100e catégorie juste avant l'enregistrement : le passage suivant recompte, et refuse.
    [Fact]
    public async Task CategoryCreatedMeanwhileAtTheLimitIsCountedOnTheNextTry()
    {
        services.AddCategoriesUpTo(99);
        services.Repository.CategoryCreatedBeforeSave = new Category { Name = "Autre requête", UserId = services.UserId };

        var result = await CreateAsync("Abonnements");

        Assert.IsType<CategoryCreationResult.TooManyCategories>(result);
        Assert.Equal(100, services.ListSavedNames().Count);
        Assert.DoesNotContain("Abonnements", services.ListSavedNames());
    }

    [Fact]
    public async Task EndlessConflictsSaveNothing()
    {
        services.Repository.Conflicts = 10;

        var result = await CreateAsync("Abonnements");

        Assert.IsType<CategoryCreationResult.NotSaved>(result);
        Assert.DoesNotContain("Abonnements", services.ListSavedNames());
    }

    // Un compte effacé pendant la requête n'a plus de catégorie « Autres » : rien n'est créé.
    [Fact]
    public async Task AccountWithoutCategoriesGetsNothing()
    {
        var result = await services.CreateCreation().CreateAsync(Guid.NewGuid(), "Abonnements");

        Assert.IsType<CategoryCreationResult.NotSaved>(result);
        Assert.Equal(0, services.UnitOfWork.CommittedTransactionCount);
    }
}
