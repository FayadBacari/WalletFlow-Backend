using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie les 8 catégories créées pour chaque nouvel utilisateur (GR Dépenses).
public class DefaultCategoriesTests
{
    [Fact]
    public void NewUserReceivesExactlyTheEightCategoriesOfTheBusinessRules()
    {
        var categories = DefaultCategories.CreateForUser(Guid.NewGuid());

        var names = new List<string>();
        foreach (var category in categories)
        {
            names.Add(category.Name);
        }

        Assert.Equal(
            new List<string> { "Logement", "Alimentation", "Transport", "Loisirs", "Santé", "Shopping", "Revenus", "Autres" },
            names);
    }

    [Fact]
    public void OnlyTheOtherCategoryCarriesTheOtherCategoryMark()
    {
        var categories = DefaultCategories.CreateForUser(Guid.NewGuid());

        foreach (var category in categories)
        {
            Assert.Equal(category.Name == "Autres", category.IsOtherCategory);
        }
    }

    [Fact]
    public void EveryDefaultCategoryBelongsToTheNewUser()
    {
        var userId = Guid.NewGuid();

        var categories = DefaultCategories.CreateForUser(userId);

        foreach (var category in categories)
        {
            Assert.Equal(userId, category.UserId);
        }
    }
}
