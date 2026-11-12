using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie ce que l'utilisateur peut changer sur une opération et sur une règle de classement (GR Comptes, GR Dépenses, DD §6.2).
public class BankTransactionTests
{
    // GR Dépenses : chaque opération appartient à une catégorie : elle ne peut pas en perdre une.
    [Fact]
    public void OperationAlwaysKeepsACategory()
    {
        var categoryId = Guid.NewGuid();
        var operation = new BankTransaction { CategoryId = Guid.NewGuid() };

        Assert.Throws<ArgumentException>(() => operation.ChangeCategory(Guid.Empty));
        operation.ChangeCategory(categoryId);

        Assert.Equal(categoryId, operation.CategoryId);
    }

    // GR Comptes : la marque de transfert se met, puis se retire.
    [Fact]
    public void TransferMarkIsSetThenRemoved()
    {
        var operation = new BankTransaction();

        operation.MarkAsInternalTransfer(true);
        Assert.True(operation.IsInternalTransfer);

        operation.MarkAsInternalTransfer(false);
        Assert.False(operation.IsInternalTransfer);
    }

    // DD §6.2 : le libellé d'une règle est déjà sous forme simple, avec au moins une lettre, et tient dans 255 caractères.
    [Theory]
    [InlineData("CB CARREFOUR", true)]
    [InlineData("A", true)]
    [InlineData("", false)]
    [InlineData("CB Carrefour", false)]
    [InlineData("CB CARREFOUR 12", false)]
    [InlineData(" CB CARREFOUR", false)]
    public void RuleLabelIsAlreadySimple(string label, bool isValid)
    {
        Assert.Equal(isValid, CategorizationRule.IsLabelValid(label));
    }

    [Fact]
    public void RuleLabelFitsTwoHundredFiftyFiveCharacters()
    {
        Assert.True(CategorizationRule.IsLabelValid(new string('A', 255)));
        Assert.False(CategorizationRule.IsLabelValid(new string('A', 256)));
    }

    // Un nouveau choix change la catégorie de la règle, qui vise toujours une catégorie.
    [Fact]
    public void RuleAlwaysKeepsACategory()
    {
        var categoryId = Guid.NewGuid();
        var rule = new CategorizationRule { Label = "CB CARREFOUR", CategoryId = Guid.NewGuid() };

        Assert.Throws<ArgumentException>(() => rule.ChangeCategory(Guid.Empty));
        rule.ChangeCategory(categoryId);

        Assert.Equal(categoryId, rule.CategoryId);
    }
}
