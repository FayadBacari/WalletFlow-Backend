using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Application;

// Vérifie la liste des catégories (GR Dépenses) : celles de l'utilisateur seulement, dans l'ordre alphabétique, « Autres » à la fin.
public class CategoryListServiceTests
{
    private readonly CategoryServices services = new CategoryServices();

    // Donne les noms de la liste, dans son ordre.
    private async Task<List<string>> ListNamesAsync()
    {
        var categories = await services.CreateList().ListAsync(services.UserId);

        return categories.Select(category => category.Name).ToList();
    }

    [Fact]
    public async Task DefaultCategoriesAreSortedWithOtherCategoryLast()
    {
        var names = await ListNamesAsync();

        Assert.Equal(
            new List<string> { "Alimentation", "Logement", "Loisirs", "Revenus", "Santé", "Shopping", "Transport", "Autres" },
            names);
    }

    // L'ordre est celui d'un dictionnaire français : ni l'accent, ni la minuscule ne rejettent un nom à la fin de la liste.
    [Fact]
    public async Task AccentsAndLowercaseLettersFollowTheFrenchOrder()
    {
        services.Repository.SavedCategories.Add(new Category { Name = "Épargne", UserId = services.UserId });
        services.Repository.SavedCategories.Add(new Category { Name = "abonnements", UserId = services.UserId });
        services.Repository.SavedCategories.Add(new Category { Name = "Zoo", UserId = services.UserId });

        var names = await ListNamesAsync();

        Assert.Equal(
            new List<string> { "abonnements", "Alimentation", "Épargne", "Logement", "Loisirs", "Revenus", "Santé", "Shopping", "Transport", "Zoo", "Autres" },
            names);
    }

    // SS §4 : les catégories d'un autre utilisateur ne sont jamais données.
    [Fact]
    public async Task CategoriesOfAnotherUserAreNeverListed()
    {
        var otherUserCategory = services.AddOtherUserCategories("Loisirs");

        var categories = await services.CreateList().ListAsync(services.UserId);

        Assert.Equal(8, categories.Count);
        Assert.DoesNotContain(categories, category => category.Id == otherUserCategory.Id);
        Assert.All(categories, category => Assert.Equal(services.UserId, category.UserId));
    }
}
