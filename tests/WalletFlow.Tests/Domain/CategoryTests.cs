using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie que la catégorie protège ses propres règles (GR Dépenses, DD §6.1), quel que soit le code qui l'appelle.
public class CategoryTests
{
    // Crée une catégorie ordinaire, avec ce nom.
    private static Category CreateCategory(string name)
    {
        return new Category { Name = name, UserId = Guid.NewGuid() };
    }

    [Fact]
    public void CategoryTakesItsNewName()
    {
        var category = CreateCategory("Loisirs");

        category.Rename("Sorties");

        Assert.Equal("Sorties", category.Name);
    }

    // GR Dépenses : « Autres » ne peut pas être renommée.
    [Fact]
    public void OtherCategoryCannotBeRenamed()
    {
        var otherCategory = new Category { Name = "Autres", IsOtherCategory = true, UserId = Guid.NewGuid() };

        Assert.Throws<InvalidOperationException>(() => otherCategory.Rename("Divers"));
        Assert.Equal("Autres", otherCategory.Name);
    }

    // Le nom arrive déjà sous sa forme enregistrée : un nom avec des espaces autour, ou invalide, est une erreur du code appelant.
    [Theory]
    [InlineData(" Sorties")]
    [InlineData("Sorties ")]
    [InlineData("")]
    public void NameNotInItsSavedFormIsRefused(string newName)
    {
        var category = CreateCategory("Loisirs");

        Assert.Throws<ArgumentException>(() => category.Rename(newName));
        Assert.Equal("Loisirs", category.Name);
    }

    [Fact]
    public void TooLongNameIsRefused()
    {
        var category = CreateCategory("Loisirs");

        Assert.Throws<ArgumentException>(() => category.Rename(new string('a', 51)));
        Assert.Equal("Loisirs", category.Name);
    }

    // Un nom écrit avec un accent séparé n'est pas sous sa forme enregistrée (NFC).
    [Fact]
    public void DecomposedNameIsRefused()
    {
        var category = CreateCategory("Loisirs");

        Assert.Throws<ArgumentException>(() => category.Rename("Sante" + (char)0x0301));
    }

    [Fact]
    public void BudgetIsSetThenRemoved()
    {
        var category = CreateCategory("Loisirs");

        category.ReplaceMonthlyBudget(3000);
        Assert.Equal(3000, category.MonthlyBudgetInCents);

        category.ReplaceMonthlyBudget(null);
        Assert.Null(category.MonthlyBudgetInCents);
    }

    // GR Dépenses : un budget par catégorie, « Autres » comprise.
    [Fact]
    public void OtherCategoryCanHaveABudget()
    {
        var otherCategory = new Category { Name = "Autres", IsOtherCategory = true, UserId = Guid.NewGuid() };

        otherCategory.ReplaceMonthlyBudget(5000);

        Assert.Equal(5000, otherCategory.MonthlyBudgetInCents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    [InlineData(1_000_000_001)]
    public void BudgetOutsideTheLimitsIsRefusedAndTheOldOneStays(int budgetInCents)
    {
        var category = CreateCategory("Loisirs");
        category.ReplaceMonthlyBudget(3000);

        Assert.Throws<ArgumentOutOfRangeException>(() => category.ReplaceMonthlyBudget(budgetInCents));
        Assert.Equal(3000, category.MonthlyBudgetInCents);
    }
}
