using WalletFlow.Application.Categories;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Application;

// Vérifie la suppression d'une catégorie (GR Dépenses, SS §4), avec une fausse base.
// Le déplacement des transactions dans la vraie base est vérifié par CategoryRepositoryTests.
public class CategoryDeletionServiceTests
{
    private readonly CategoryServices services = new CategoryServices();

    // Supprime cette catégorie pour l'utilisateur des tests.
    private async Task<CategoryDeletionResult> DeleteAsync(Guid categoryId)
    {
        return await services.CreateDeletion().DeleteAsync(services.UserId, categoryId);
    }

    // GR Dépenses : supprimer une catégorie déplace ses transactions dans « Autres ». Ses règles disparaissent avec elle.
    [Fact]
    public async Task CategoryIsDeletedAndItsTransactionsGoToOther()
    {
        var leisure = services.Find("Loisirs");
        var health = services.Find("Santé");
        var cinema = services.AddTransaction(leisure, "CINEMA");
        var pharmacy = services.AddTransaction(health, "PHARMACIE");
        services.Repository.SavedRules.Add(new CategorizationRule { Label = "CINEMA", CategoryId = leisure.Id });

        var result = await DeleteAsync(leisure.Id);

        Assert.IsType<CategoryDeletionResult.Deleted>(result);
        Assert.DoesNotContain("Loisirs", services.ListSavedNames());
        Assert.Equal(services.Find("Autres").Id, cinema.CategoryId);
        Assert.Equal(health.Id, pharmacy.CategoryId);
        Assert.Empty(services.Repository.SavedRules);
    }

    // GR Dépenses : « Autres » ne peut pas être supprimée, puisqu'elle reçoit les transactions des catégories supprimées.
    [Fact]
    public async Task OtherCategoryIsNeverDeleted()
    {
        var result = await DeleteAsync(services.Find("Autres").Id);

        Assert.IsType<CategoryDeletionResult.OtherCategoryIsFixed>(result);
        Assert.Contains("Autres", services.ListSavedNames());
    }

    // SS §4 : la catégorie d'un autre utilisateur n'est pas trouvée, et elle reste.
    [Fact]
    public async Task CategoryOfAnotherUserIsNotFoundAndStays()
    {
        var otherUserCategory = services.AddOtherUserCategories("Loisirs");

        var result = await DeleteAsync(otherUserCategory.Id);

        Assert.IsType<CategoryDeletionResult.NotFound>(result);
        Assert.Contains(otherUserCategory, services.Repository.SavedCategories);
    }

    [Fact]
    public async Task UnknownCategoryIsNotFound()
    {
        var result = await DeleteAsync(Guid.NewGuid());

        Assert.IsType<CategoryDeletionResult.NotFound>(result);
        Assert.Equal(8, services.ListSavedNames().Count);
    }

    // Une autre requête la supprime entre sa lecture et sa suppression : la suppression ne trouve plus rien.
    [Fact]
    public async Task CategoryDeletedMeanwhileIsNotFound()
    {
        services.Repository.CategoryDisappearsBeforeDeletion = true;

        var result = await DeleteAsync(services.Find("Loisirs").Id);

        Assert.IsType<CategoryDeletionResult.NotFound>(result);
    }

    [Fact]
    public async Task SecondDeletionOfTheSameCategoryIsNotFound()
    {
        var leisure = services.Find("Loisirs");
        await DeleteAsync(leisure.Id);

        var result = await DeleteAsync(leisure.Id);

        Assert.IsType<CategoryDeletionResult.NotFound>(result);
    }
}
