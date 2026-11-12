using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie le choix de la catégorie d'une opération qui arrive de la banque (GR Dépenses, DD §6.2).
public class CategoryChoiceTests
{
    private readonly Guid userId = Guid.NewGuid();
    private readonly List<Category> categories;

    // Les 8 catégories par défaut de l'utilisateur des tests.
    public CategoryChoiceTests()
    {
        categories = DefaultCategories.CreateForUser(userId);
    }

    // Donne la catégorie de l'utilisateur qui porte ce nom.
    private Category Named(string name)
    {
        return categories.Single(category => category.Name == name);
    }

    // GR Dépenses : une règle de l'utilisateur l'emporte sur une règle commune.
    [Fact]
    public void UserRuleBeatsCommonRule()
    {
        var rules = new List<CategorizationRule> { new CategorizationRule { Label = "CB CARREFOUR", CategoryId = Named("Loisirs").Id } };

        Assert.Equal(Named("Loisirs").Id, CategoryChoice.ForArrivingTransaction("CB CARREFOUR 12/11", rules, categories));
    }

    // DD §6.2 : la règle de l'utilisateur reconnaît le même libellé, chiffres et ponctuation mis à part.
    [Fact]
    public void UserRuleRecognizesTheSameLabelWithOtherDigits()
    {
        var rules = new List<CategorizationRule> { new CategorizationRule { Label = "PRLV ABONNEMENT", CategoryId = Named("Loisirs").Id } };

        Assert.Equal(Named("Loisirs").Id, CategoryChoice.ForArrivingTransaction("prlv abonnement 4512 / 03-2026", rules, categories));
        Assert.Equal(Named("Autres").Id, CategoryChoice.ForArrivingTransaction("PRLV ABONNEMENT TELEPHONE", rules, categories));
    }

    // GR Dépenses : sans règle de l'utilisateur, la règle commune donne sa catégorie.
    [Fact]
    public void CommonRuleGivesItsCategory()
    {
        Assert.Equal(Named("Alimentation").Id, CategoryChoice.ForArrivingTransaction("CB CARREFOUR 12/11", new List<CategorizationRule>(), categories));
    }

    // DD §6.2 : une règle commune vise sa catégorie par son nom. Renommée, l'opération va dans « Autres ».
    [Fact]
    public void RenamedCategoryLeavesTheOperationInOthers()
    {
        Named("Alimentation").Rename("Courses");

        Assert.Equal(Named("Autres").Id, CategoryChoice.ForArrivingTransaction("CB CARREFOUR 12/11", new List<CategorizationRule>(), categories));
    }

    // DD §6.2 : supprimée, l'opération va dans « Autres ». Recréée avec d'autres majuscules, elle est reconnue,
    // comme l'unicité des noms ne tient pas compte des majuscules (DD §6.1).
    [Fact]
    public void CategoryIsFoundByItsNameIgnoringCase()
    {
        categories.Remove(Named("Alimentation"));
        Assert.Equal(Named("Autres").Id, CategoryChoice.ForArrivingTransaction("CB CARREFOUR", new List<CategorizationRule>(), categories));

        var recreated = new Category { Name = "ALIMENTATION", UserId = userId };
        categories.Add(recreated);
        Assert.Equal(recreated.Id, CategoryChoice.ForArrivingTransaction("CB CARREFOUR", new List<CategorizationRule>(), categories));
    }

    // Une règle d'exception envoie dans « Autres » ce que la règle plus courte classerait mal : un remboursement de crédit
    // de « CARREFOUR BANQUE » n'est pas une course chez Carrefour.
    [Fact]
    public void ExceptionRuleSendsToOthers()
    {
        Assert.Equal(Named("Autres").Id, CategoryChoice.ForArrivingTransaction("PRLV CARREFOUR BANQUE", new List<CategorizationRule>(), categories));
        Assert.Equal(Named("Alimentation").Id, CategoryChoice.ForArrivingTransaction("CB CARREFOUR MARKET", new List<CategorizationRule>(), categories));
    }

    // GR Dépenses : si aucune catégorie ne correspond, l'opération est classée dans « Autres ».
    [Fact]
    public void UnknownLabelGoesToOthers()
    {
        Assert.Equal(Named("Autres").Id, CategoryChoice.ForArrivingTransaction("VIR M DUPONT", new List<CategorizationRule>(), categories));
        Assert.Equal(Named("Autres").Id, CategoryChoice.ForArrivingTransaction("12/11", new List<CategorizationRule>(), categories));
    }

    // Une règle dont la catégorie n'est plus là ne sert pas : la règle commune, puis « Autres », prennent le relais.
    [Fact]
    public void RuleOfAMissingCategoryIsIgnored()
    {
        var rules = new List<CategorizationRule> { new CategorizationRule { Label = "CB CARREFOUR", CategoryId = Guid.NewGuid() } };

        Assert.Equal(Named("Alimentation").Id, CategoryChoice.ForArrivingTransaction("CB CARREFOUR", rules, categories));
    }

    // Sans « Autres », le compte vient d'être effacé : aucune catégorie n'est choisie.
    [Fact]
    public void NoCategoryIsChosenWithoutOthers()
    {
        categories.Remove(Named("Autres"));

        Assert.Null(CategoryChoice.ForArrivingTransaction("CB CARREFOUR", new List<CategorizationRule>(), categories));
    }
}
