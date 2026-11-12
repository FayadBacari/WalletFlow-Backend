using WalletFlow.Application.Persistence;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le changement de catégorie d'une opération, et la règle des prochaines opérations de même libellé
// (GR Dépenses, SS §4, DD §6.2), avec un faux prestataire et une fausse base.
public class TransactionCategoryChangeServiceTests
{
    private readonly BankServices services = new BankServices();

    // Connecte un compte avec une opération par libellé, un jour après l'autre, et renvoie les opérations gardées.
    private async Task<List<BankTransaction>> ConnectWithAsync(params string[] labels)
    {
        var day = services.Today.AddDays(-labels.Length);

        foreach (var label in labels)
        {
            services.Provider.AddTransaction("stable-1", day, -1000, label);
            day = day.AddDays(1);
        }

        var connection = await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000));

        return services.TransactionsOf(services.AccountsOf(connection).Single());
    }

    // Change la catégorie d'une opération pour l'utilisateur des tests.
    private async Task<TransactionCategoryChangeResult> ChangeAsync(BankTransaction transaction, Category category, bool applyToFutureTransactions)
    {
        return await services.CreateCategoryChange().ChangeAsync(services.UserId, transaction.Id, category.Id, applyToFutureTransactions);
    }

    // GR Dépenses : sans règle, seule l'opération choisie change de catégorie, et la réponse la montre telle que l'utilisateur la voit.
    [Fact]
    public async Task ChangeWithoutRuleChangesOnlyThisOperation()
    {
        var transactions = await ConnectWithAsync("CB CARREFOUR 01/11", "CB CARREFOUR 02/11");
        var leisure = services.CategoryNamed("Loisirs");

        var result = await ChangeAsync(transactions[0], leisure, false);

        var changed = Assert.IsType<TransactionCategoryChangeResult.Changed>(result);
        Assert.Equal(transactions[0].Id, changed.Transaction.Id);
        Assert.Equal(leisure.Id, changed.Transaction.CategoryId);
        Assert.Equal("Loisirs", changed.Transaction.CategoryName);
        Assert.Equal("Compte courant", changed.Transaction.AccountName);
        Assert.Equal(leisure.Id, transactions[0].CategoryId);
        Assert.Equal(services.CategoryNamed("Alimentation").Id, transactions[1].CategoryId);
        Assert.Empty(services.Transactions.SavedRules);
    }

    // GR Dépenses et DD §6.2 : la règle garde la forme simple du libellé. Les opérations déjà reçues ne changent pas :
    // la règle vaut pour les prochaines. « Autres » est réécrite, pour qu'une seule règle existe par libellé.
    [Fact]
    public async Task ApplyToFutureCreatesOneRuleAndKeepsPastOperations()
    {
        var transactions = await ConnectWithAsync("CB CARREFOUR 01/11", "CB CARREFOUR 02/11");
        var leisure = services.CategoryNamed("Loisirs");

        Assert.IsType<TransactionCategoryChangeResult.Changed>(await ChangeAsync(transactions[0], leisure, true));

        var rule = Assert.Single(services.Transactions.SavedRules);
        Assert.Equal("CB CARREFOUR", rule.Label);
        Assert.Equal(leisure.Id, rule.CategoryId);
        Assert.Equal(services.CategoryNamed("Alimentation").Id, transactions[1].CategoryId);
        Assert.Contains(services.CategoryNamed("Autres"), services.Categories.CheckedCategories);
    }

    // Un nouveau choix pour le même libellé remplace l'ancien : il reste une seule règle.
    [Fact]
    public async Task NewChoiceReplacesTheRule()
    {
        var transactions = await ConnectWithAsync("CB CARREFOUR 01/11", "CB CARREFOUR 02/11");

        await ChangeAsync(transactions[0], services.CategoryNamed("Loisirs"), true);
        await ChangeAsync(transactions[1], services.CategoryNamed("Shopping"), true);

        Assert.Equal(services.CategoryNamed("Shopping").Id, Assert.Single(services.Transactions.SavedRules).CategoryId);
    }

    // SS §4 : l'opération d'un autre utilisateur n'est pas trouvée, comme une opération qui n'existe pas.
    [Fact]
    public async Task OperationOfAnotherUserIsNotFound()
    {
        var transactions = await ConnectWithAsync("CB CARREFOUR");
        var leisure = services.CategoryNamed("Loisirs");

        var otherUser = await services.CreateCategoryChange().ChangeAsync(Guid.NewGuid(), transactions[0].Id, leisure.Id, true);
        var unknown = await services.CreateCategoryChange().ChangeAsync(services.UserId, Guid.NewGuid(), leisure.Id, true);

        Assert.IsType<TransactionCategoryChangeResult.TransactionNotFound>(otherUser);
        Assert.IsType<TransactionCategoryChangeResult.TransactionNotFound>(unknown);
        Assert.Equal(services.CategoryNamed("Alimentation").Id, transactions[0].CategoryId);
        Assert.Empty(services.Transactions.SavedRules);
    }

    // SS §4 : la catégorie d'un autre utilisateur n'est pas trouvée : l'opération ne peut jamais y être rangée.
    [Fact]
    public async Task CategoryOfAnotherUserIsNotFound()
    {
        var transactions = await ConnectWithAsync("CB CARREFOUR");
        var otherCategories = DefaultCategories.CreateForUser(Guid.NewGuid());
        services.Categories.SavedCategories.AddRange(otherCategories);

        var result = await ChangeAsync(transactions[0], otherCategories[0], true);

        Assert.IsType<TransactionCategoryChangeResult.CategoryNotFound>(result);
        Assert.Equal(services.CategoryNamed("Alimentation").Id, transactions[0].CategoryId);
        Assert.Empty(services.Transactions.SavedRules);
    }

    // Un libellé sans aucune lettre ne peut pas avoir de règle : la demande est refusée, et rien ne change.
    // Sans règle, le changement de catégorie reste possible.
    [Fact]
    public async Task LabelWithoutLetterRefusesOnlyTheRule()
    {
        var transactions = await ConnectWithAsync("12345 / 06-11");
        var leisure = services.CategoryNamed("Loisirs");

        Assert.IsType<TransactionCategoryChangeResult.LabelWithoutLetter>(await ChangeAsync(transactions[0], leisure, true));
        Assert.Equal(services.CategoryNamed("Autres").Id, transactions[0].CategoryId);

        Assert.IsType<TransactionCategoryChangeResult.Changed>(await ChangeAsync(transactions[0], leisure, false));
        Assert.Equal(leisure.Id, transactions[0].CategoryId);
        Assert.Empty(services.Transactions.SavedRules);
    }

    // Une autre requête crée au même instant la règle du même libellé : l'enregistrement échoue, relit,
    // et change cette règle au lieu d'en créer une seconde.
    [Fact]
    public async Task RuleCreatedMeanwhileIsChangedInsteadOfDuplicated()
    {
        var transactions = await ConnectWithAsync("CB CARREFOUR 01/11");
        var leisure = services.CategoryNamed("Loisirs");
        services.Transactions.RuleCreatedBeforeSave = new CategorizationRule { Label = "CB CARREFOUR", CategoryId = services.CategoryNamed("Shopping").Id };

        Assert.IsType<TransactionCategoryChangeResult.Changed>(await ChangeAsync(transactions[0], leisure, true));

        Assert.Equal(leisure.Id, Assert.Single(services.Transactions.SavedRules).CategoryId);
        Assert.Equal(leisure.Id, transactions[0].CategoryId);
    }

    // Si d'autres requêtes changent sans arrêt les mêmes lignes, rien n'est enregistré, et l'opération reste comme avant.
    [Fact]
    public async Task ConstantConflictsSaveNothing()
    {
        var transactions = await ConnectWithAsync("CB CARREFOUR");
        services.Transactions.Conflicts = DatabaseChangesSavingService.MaximumTries;

        var result = await ChangeAsync(transactions[0], services.CategoryNamed("Loisirs"), true);

        Assert.IsType<TransactionCategoryChangeResult.NotSaved>(result);
        Assert.Equal(services.CategoryNamed("Alimentation").Id, transactions[0].CategoryId);
        Assert.Empty(services.Transactions.SavedRules);
    }
}
