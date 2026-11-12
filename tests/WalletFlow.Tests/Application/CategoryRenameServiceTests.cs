using WalletFlow.Application.Categories;

namespace WalletFlow.Tests.Application;

// Vérifie le changement de nom d'une catégorie (GR Dépenses, SS §4, DD §6.1), avec une fausse base.
public class CategoryRenameServiceTests
{
    private readonly CategoryServices services = new CategoryServices();

    // Renomme cette catégorie pour l'utilisateur des tests.
    private async Task<CategoryRenameResult> RenameAsync(Guid categoryId, string? typedName)
    {
        return await services.CreateRename().RenameAsync(services.UserId, categoryId, typedName);
    }

    [Fact]
    public async Task CategoryTakesItsNewSavedName()
    {
        var category = services.Find("Loisirs");

        var result = await RenameAsync(category.Id, " Sorties  ");

        var renamed = Assert.IsType<CategoryRenameResult.Renamed>(result);
        Assert.Same(category, renamed.Category);
        Assert.Equal("Sorties", category.Name);
        Assert.Equal(1, services.UnitOfWork.CommittedTransactionCount);
    }

    // GR Dépenses : « Autres » ne peut pas être renommée.
    [Fact]
    public async Task OtherCategoryIsNeverRenamed()
    {
        var otherCategory = services.Find("Autres");

        var result = await RenameAsync(otherCategory.Id, "Divers");

        Assert.IsType<CategoryRenameResult.OtherCategoryIsFixed>(result);
        Assert.Equal("Autres", otherCategory.Name);
    }

    // SS §4 : la catégorie d'un autre utilisateur n'est pas trouvée, et ne change pas.
    [Fact]
    public async Task CategoryOfAnotherUserIsNotFoundAndKeepsItsName()
    {
        var otherUserCategory = services.AddOtherUserCategories("Loisirs");

        var result = await RenameAsync(otherUserCategory.Id, "Piratée");

        Assert.IsType<CategoryRenameResult.NotFound>(result);
        Assert.Equal("Loisirs", otherUserCategory.Name);
    }

    [Fact]
    public async Task UnknownCategoryIsNotFound()
    {
        var result = await RenameAsync(Guid.NewGuid(), "Sorties");

        Assert.IsType<CategoryRenameResult.NotFound>(result);
    }

    // DD §6.1 : le nom d'une autre catégorie de l'utilisateur est refusé, majuscules et accents compris.
    [Theory]
    [InlineData("SANTÉ")]
    [InlineData("santé")]
    [InlineData("Autres")]
    public async Task NameOfAnotherCategoryIsRefused(string typedName)
    {
        var category = services.Find("Loisirs");

        var result = await RenameAsync(category.Id, typedName);

        Assert.IsType<CategoryRenameResult.NameAlreadyUsed>(result);
        Assert.Equal("Loisirs", category.Name);
    }

    // Changer seulement les majuscules de son propre nom est permis : la catégorie ne se compare pas à elle-même.
    [Theory]
    [InlineData("LOISIRS")]
    [InlineData("Loisirs")]
    public async Task CategoryCanKeepItsNameOrChangeItsCase(string typedName)
    {
        var category = services.Find("Loisirs");

        var result = await RenameAsync(category.Id, typedName);

        Assert.IsType<CategoryRenameResult.Renamed>(result);
        Assert.Equal(typedName, category.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task InvalidNameIsRefusedBeforeReadingAnything(string? typedName)
    {
        var category = services.Find("Loisirs");

        var result = await RenameAsync(category.Id, typedName);

        Assert.IsType<CategoryRenameResult.RefusedByRules>(result);
        Assert.Equal(0, services.Repository.SearchCount);
        Assert.Equal("Loisirs", category.Name);
    }

    // Une autre requête supprime la catégorie juste avant l'enregistrement : le passage suivant ne la trouve plus.
    [Fact]
    public async Task CategoryDeletedMeanwhileIsNotFoundOnTheNextTry()
    {
        var category = services.Find("Loisirs");
        services.Repository.DeleteReadCategoryBeforeSave = true;

        var result = await RenameAsync(category.Id, "Sorties");

        Assert.IsType<CategoryRenameResult.NotFound>(result);
        Assert.DoesNotContain("Sorties", services.ListSavedNames());
        Assert.DoesNotContain("Loisirs", services.ListSavedNames());
    }

    // Les conflits sans fin n'enregistrent rien : la catégorie reprend son nom, comme après une transaction annulée.
    [Fact]
    public async Task EndlessConflictsKeepTheOldName()
    {
        var category = services.Find("Loisirs");
        services.Repository.Conflicts = 10;

        var result = await RenameAsync(category.Id, "Sorties");

        Assert.IsType<CategoryRenameResult.NotSaved>(result);
        Assert.Equal("Loisirs", category.Name);
    }
}
