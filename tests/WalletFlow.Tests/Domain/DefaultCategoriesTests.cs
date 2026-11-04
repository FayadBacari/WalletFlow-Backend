using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie les 8 catégories créées pour chaque nouvel utilisateur (GR Dépenses).
public class DefaultCategoriesTests
{
    [Fact]
    public void NewUserReceivesExactlyTheEightCategoriesOfTheBusinessRules()
    {
        var defaultCategoriesCreatedForTheNewUser = DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(Guid.NewGuid());

        var namesOfTheCategoriesCreatedForTheNewUser = new List<string>();
        foreach (var categoryCreatedForTheNewUser in defaultCategoriesCreatedForTheNewUser)
        {
            namesOfTheCategoriesCreatedForTheNewUser.Add(categoryCreatedForTheNewUser.Name);
        }

        Assert.Equal(
            new List<string> { "Logement", "Alimentation", "Transport", "Loisirs", "Santé", "Shopping", "Revenus", "Autres" },
            namesOfTheCategoriesCreatedForTheNewUser);
    }

    [Fact]
    public void OnlyTheOtherCategoryCarriesTheOtherCategoryMark()
    {
        var defaultCategoriesCreatedForTheNewUser = DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(Guid.NewGuid());

        foreach (var categoryCreatedForTheNewUser in defaultCategoriesCreatedForTheNewUser)
        {
            Assert.Equal(categoryCreatedForTheNewUser.Name == "Autres", categoryCreatedForTheNewUser.IsOtherCategory);
        }
    }

    [Fact]
    public void EveryDefaultCategoryBelongsToTheNewUser()
    {
        var identifierOfTheNewlyRegisteredUser = Guid.NewGuid();

        var defaultCategoriesCreatedForTheNewUser = DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(identifierOfTheNewlyRegisteredUser);

        foreach (var categoryCreatedForTheNewUser in defaultCategoriesCreatedForTheNewUser)
        {
            Assert.Equal(identifierOfTheNewlyRegisteredUser, categoryCreatedForTheNewUser.UserId);
        }
    }
}
