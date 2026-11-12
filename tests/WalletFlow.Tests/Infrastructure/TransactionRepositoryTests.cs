using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Tests.EndToEnd;
using static WalletFlow.Tests.Infrastructure.BankTestRows;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les opérations bancaires et les règles de classement dans la vraie base PostgreSQL locale, avec l'utilisateur
// de l'API (GR Comptes, GR Dépenses, SS §4 et §5, MPD). Les tests de lecture travaillent dans une transaction annulée à la fin.
// Les tests de conflit enregistrent pour de vrai, car une autre requête doit voir leurs lignes : ils effacent leur compte à la fin.
public class TransactionRepositoryTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    // Crée la recherche de la première page, avec ces critères.
    private static TransactionSearch Search(
        string? text = null,
        int? amountInCents = null,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? accountId = null,
        Guid? categoryId = null,
        int page = 1,
        int pageSize = 50)
    {
        return new TransactionSearch(text, amountInCents, from, to, accountId, categoryId, page, pageSize);
    }

    // Donne les libellés trouvés, dans l'ordre de la réponse.
    private static async Task<List<string>> SearchLabelsAsync(WalletFlowDatabaseContext databaseContext, Guid userId, TransactionSearch search)
    {
        var page = await new TransactionRepository(databaseContext).SearchAsync(userId, search);

        return page.Items.Select(item => item.Label).ToList();
    }

    // Donne la catégorie de l'utilisateur qui porte ce nom, sans la suivre.
    private static async Task<Category> FindCategoryAsync(WalletFlowDatabaseContext databaseContext, Guid userId, string name)
    {
        return await databaseContext.Categories.AsNoTracking().SingleAsync(category => category.UserId == userId && category.Name == name);
    }

    // SS §5 : le texte cherché est pris tel quel : %, _ et \ ne sont pas des jokers. Les majuscules ne comptent pas.
    [Fact]
    public async Task TextIsSearchedLiterallyIgnoringCase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-operations-texte@exemple.test");
        var (_, account) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");
        var others = await FindCategoryAsync(databaseContext, user.Id, "Autres");
        await SaveTransactionsAsync(databaseContext,
            CreateTransaction(account.Id, others.Id, "e1", "PAIEMENT 100% REMBOURSE", date: Today.AddDays(-1)),
            CreateTransaction(account.Id, others.Id, "e2", "PAIEMENT 1000 EUROS", date: Today.AddDays(-2)),
            CreateTransaction(account.Id, others.Id, "e3", "cb_carrefour", date: Today.AddDays(-3)),
            CreateTransaction(account.Id, others.Id, "e4", "CB XCARREFOUR", date: Today.AddDays(-4)),
            CreateTransaction(account.Id, others.Id, "e5", "Carrefour Market", date: Today.AddDays(-5)),
            CreateTransaction(account.Id, others.Id, "e6", "VIR C:\\DOSSIER", date: Today.AddDays(-6)));

        Assert.Equal(new List<string> { "PAIEMENT 100% REMBOURSE" }, await SearchLabelsAsync(databaseContext, user.Id, Search("100%")));
        Assert.Equal(new List<string> { "cb_carrefour" }, await SearchLabelsAsync(databaseContext, user.Id, Search("_")));
        Assert.Equal(new List<string> { "VIR C:\\DOSSIER" }, await SearchLabelsAsync(databaseContext, user.Id, Search("\\D")));
        Assert.Equal(
            new List<string> { "cb_carrefour", "CB XCARREFOUR", "Carrefour Market" },
            await SearchLabelsAsync(databaseContext, user.Id, Search("CARREFOUR")));
    }

    // GR Comptes : par montant sans son signe, par dates comprises, par compte et par catégorie.
    [Fact]
    public async Task EachCriterionKeepsItsOperations()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-operations-criteres@exemple.test");
        var (_, current) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");
        var (_, savings) = await SaveConnectionAsync(databaseContext, user.Id, "stable-2");
        var food = await FindCategoryAsync(databaseContext, user.Id, "Alimentation");
        var others = await FindCategoryAsync(databaseContext, user.Id, "Autres");
        await SaveTransactionsAsync(databaseContext,
            CreateTransaction(current.Id, food.Id, "e1", "CB CARREFOUR", -1250, Today.AddDays(-3)),
            CreateTransaction(current.Id, others.Id, "e2", "REMBOURSEMENT", 1250, Today.AddDays(-2)),
            CreateTransaction(current.Id, others.Id, "e3", "CB SNCF", -4000, Today.AddDays(-1)),
            CreateTransaction(savings.Id, others.Id, "e4", "INTERETS", 10_000, Today));

        Assert.Equal(new List<string> { "REMBOURSEMENT", "CB CARREFOUR" }, await SearchLabelsAsync(databaseContext, user.Id, Search(amountInCents: 1250)));
        Assert.Equal(
            new List<string> { "CB SNCF", "REMBOURSEMENT" },
            await SearchLabelsAsync(databaseContext, user.Id, Search(from: Today.AddDays(-2), to: Today.AddDays(-1))));
        Assert.Equal(new List<string> { "INTERETS" }, await SearchLabelsAsync(databaseContext, user.Id, Search(accountId: savings.Id)));
        Assert.Equal(new List<string> { "CB CARREFOUR" }, await SearchLabelsAsync(databaseContext, user.Id, Search(categoryId: food.Id)));
        Assert.Equal(new List<string> { "CB SNCF" }, await SearchLabelsAsync(databaseContext, user.Id, Search(text: "cb", amountInCents: 4000)));
    }

    // Les plus récentes d'abord, puis toujours le même ordre d'une page à l'autre : chaque opération vient une seule fois.
    [Fact]
    public async Task PagesFollowEachOtherWithoutRepeatingAnOperation()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-operations-pages@exemple.test");
        var (_, account) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");
        var others = await FindCategoryAsync(databaseContext, user.Id, "Autres");
        var operations = new List<BankTransaction>();

        for (var number = 0; number < 25; number++)
        {
            operations.Add(CreateTransaction(account.Id, others.Id, "e" + number, "CB CAFE " + number, -100, Today.AddDays(-(number % 4))));
        }

        await SaveTransactionsAsync(databaseContext, operations.ToArray());
        var repository = new TransactionRepository(databaseContext);
        var pages = new List<TransactionPage>();

        for (var page = 1; page <= 3; page++)
        {
            pages.Add(await repository.SearchAsync(user.Id, Search(page: page, pageSize: 10)));
        }

        var items = pages.SelectMany(page => page.Items).ToList();
        Assert.Equal(new List<int> { 10, 10, 5 }, pages.Select(page => page.Items.Count).ToList());
        Assert.All(pages, page => Assert.Equal(25, page.TotalCount));
        Assert.Equal(25, items.Select(item => item.Id).Distinct().Count());
        Assert.Equal(items.Select(item => item.Date).OrderByDescending(date => date).ToList(), items.Select(item => item.Date).ToList());
    }

    // SS §4 : l'opération d'un autre utilisateur n'est jamais trouvée, ni lue, ni cherchée.
    [Fact]
    public async Task OperationOfAnotherUserIsNeverFound()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var owner = await SaveAccountAsync(databaseContext, "test-operations-a@exemple.test");
        var stranger = await SaveAccountAsync(databaseContext, "test-operations-b@exemple.test");
        var (_, account) = await SaveConnectionAsync(databaseContext, owner.Id, "stable-1");
        var others = await FindCategoryAsync(databaseContext, owner.Id, "Autres");
        var operation = CreateTransaction(account.Id, others.Id, "e1");
        await SaveTransactionsAsync(databaseContext, operation);
        var repository = new TransactionRepository(databaseContext);

        Assert.Null(await repository.FindAsync(stranger.Id, operation.Id));
        Assert.Null(await repository.ReadViewAsync(stranger.Id, operation.Id));
        Assert.Empty((await repository.SearchAsync(stranger.Id, Search())).Items);
        Assert.Equal(0, (await repository.SearchAsync(stranger.Id, Search(accountId: account.Id))).TotalCount);
        Assert.NotNull(await repository.FindAsync(owner.Id, operation.Id));

        var view = await repository.ReadViewAsync(owner.Id, operation.Id);
        Assert.Equal("Compte courant", view!.AccountName);
        Assert.Equal("Autres", view.CategoryName);
        Assert.Equal(account.Id, view.AccountId);
    }

    // La dernière date et les identifiants connus sont relus en base, pour ce compte seulement.
    [Fact]
    public async Task LatestDateAndKnownIdentifiersAreReadForTheAccount()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-operations-connues@exemple.test");
        var (_, current) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");
        var (_, savings) = await SaveConnectionAsync(databaseContext, user.Id, "stable-2");
        var others = await FindCategoryAsync(databaseContext, user.Id, "Autres");
        await SaveTransactionsAsync(databaseContext,
            CreateTransaction(current.Id, others.Id, "ancienne", date: Today.AddDays(-30)),
            CreateTransaction(current.Id, others.Id, "recente", date: Today.AddDays(-2)),
            CreateTransaction(savings.Id, others.Id, "livret", date: Today));
        var repository = new TransactionRepository(databaseContext);

        Assert.Equal(Today.AddDays(-2), await repository.FindLatestDateAsync(current.Id));
        Assert.Null(await repository.FindLatestDateAsync(Guid.NewGuid()));
        Assert.Equal(new HashSet<string> { "recente" }, await repository.FindExternalIdsAsync(current.Id, Today.AddDays(-2)));
        Assert.Equal(new HashSet<string> { "ancienne", "recente" }, await repository.FindExternalIdsAsync(current.Id, Today.AddDays(-30)));
    }

    // GR Dépenses : les règles d'un utilisateur sont celles de ses catégories, jamais celles d'un autre.
    [Fact]
    public async Task RulesAreTheOnesOfTheUserCategories()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var owner = await SaveAccountAsync(databaseContext, "test-regles-a@exemple.test");
        var stranger = await SaveAccountAsync(databaseContext, "test-regles-b@exemple.test");
        var ownerLeisure = await FindCategoryAsync(databaseContext, owner.Id, "Loisirs");
        var strangerLeisure = await FindCategoryAsync(databaseContext, stranger.Id, "Loisirs");
        databaseContext.CategorizationRules.AddRange(
            new CategorizationRule { Label = "CB CARREFOUR", CategoryId = ownerLeisure.Id },
            new CategorizationRule { Label = "CB SNCF", CategoryId = strangerLeisure.Id });
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
        var repository = new TransactionRepository(databaseContext);

        Assert.Equal("CB CARREFOUR", Assert.Single(await repository.FindRulesAsync(owner.Id)).Label);
        Assert.NotNull(await repository.FindRuleAsync(owner.Id, "CB CARREFOUR"));
        Assert.Null(await repository.FindRuleAsync(owner.Id, "CB SNCF"));
        Assert.Null(await repository.FindRuleAsync(stranger.Id, "CB CARREFOUR"));
    }

    // GR Dépenses et GR Comptes : la base additionne par catégorie et par mois civil, sans les transferts, pour les seuls
    // comptes de l'utilisateur, ou un seul. Les montants sont additionnés en bigint : deux milliards de centimes ne débordent pas.
    [Fact]
    public async Task SumsAreMadeByCategoryAndMonthWithoutTransfers()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var owner = await SaveAccountAsync(databaseContext, "test-graphiques-a@exemple.test");
        var stranger = await SaveAccountAsync(databaseContext, "test-graphiques-b@exemple.test");
        var (_, current) = await SaveConnectionAsync(databaseContext, owner.Id, "stable-1");
        var (_, savings) = await SaveConnectionAsync(databaseContext, owner.Id, "stable-2");
        var (_, strangerAccount) = await SaveConnectionAsync(databaseContext, stranger.Id, "stable-3");
        var food = await FindCategoryAsync(databaseContext, owner.Id, "Alimentation");
        var income = await FindCategoryAsync(databaseContext, owner.Id, "Revenus");
        var others = await FindCategoryAsync(databaseContext, owner.Id, "Autres");
        var strangerFood = await FindCategoryAsync(databaseContext, stranger.Id, "Alimentation");
        var transfer = CreateTransaction(current.Id, others.Id, "e6", "VIR VERS LIVRET", -50_000, new DateOnly(2026, 11, 10));
        transfer.MarkAsInternalTransfer(true);
        await SaveTransactionsAsync(databaseContext,
            CreateTransaction(current.Id, food.Id, "e1", "CB CARREFOUR", -1000, new DateOnly(2026, 10, 31)),
            CreateTransaction(current.Id, food.Id, "e2", "CB CARREFOUR", -2000, new DateOnly(2026, 11, 1)),
            CreateTransaction(current.Id, food.Id, "e3", "REMBOURSEMENT CARREFOUR", 1500, new DateOnly(2026, 11, 30)),
            CreateTransaction(current.Id, income.Id, "e4", "VIR SALAIRE", 250_000, new DateOnly(2026, 11, 15)),
            CreateTransaction(savings.Id, food.Id, "e5", "CB CARREFOUR", -4000, new DateOnly(2026, 11, 5)),
            transfer,
            CreateTransaction(current.Id, income.Id, "e7", "VIR PRIME", 1_000_000_000, new DateOnly(2026, 12, 1)),
            CreateTransaction(current.Id, income.Id, "e8", "VIR PRIME", 1_000_000_000, new DateOnly(2026, 12, 2)),
            CreateTransaction(strangerAccount.Id, strangerFood.Id, "e9", "CB CARREFOUR", -7777, new DateOnly(2026, 11, 5)));
        var repository = new TransactionRepository(databaseContext);

        var all = await repository.SumByCategoryAndMonthAsync(owner.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), null);
        var currentOnly = await repository.SumByCategoryAndMonthAsync(owner.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), current.Id);
        var november = await repository.SumByCategoryAndMonthAsync(owner.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null);

        Assert.Equal(
            new List<CategoryMonthTotal>
            {
                new CategoryMonthTotal(food.Id, 2026, 10, -1000),
                new CategoryMonthTotal(food.Id, 2026, 11, -4500),
                new CategoryMonthTotal(income.Id, 2026, 11, 250_000),
                new CategoryMonthTotal(income.Id, 2026, 12, 2_000_000_000)
            },
            all.OrderBy(total => total.Month).ThenBy(total => total.NetInCents).ToList());
        Assert.Contains(new CategoryMonthTotal(food.Id, 2026, 11, -500), currentOnly);
        Assert.DoesNotContain(currentOnly, total => total.Month == 11 && total.NetInCents == -4500);

        // Les deux dates sont comprises : le 1er et le 30 novembre comptent, le 31 octobre et le 1er décembre non.
        Assert.Equal(
            new List<CategoryMonthTotal>
            {
                new CategoryMonthTotal(food.Id, 2026, 11, -4500),
                new CategoryMonthTotal(income.Id, 2026, 11, 250_000)
            },
            november.OrderBy(total => total.NetInCents).ToList());
        Assert.Empty(await repository.SumByCategoryAndMonthAsync(stranger.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), current.Id));
    }

    // La plus ancienne opération dit jusqu'où remonte l'historique : un transfert compte aussi, mais jamais le compte d'un autre.
    [Fact]
    public async Task EarliestDateCountsTransfersButOnlyTheUserAccounts()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var owner = await SaveAccountAsync(databaseContext, "test-historique-a@exemple.test");
        var stranger = await SaveAccountAsync(databaseContext, "test-historique-b@exemple.test");
        var (_, current) = await SaveConnectionAsync(databaseContext, owner.Id, "stable-1");
        var (_, savings) = await SaveConnectionAsync(databaseContext, owner.Id, "stable-2");
        var (_, strangerAccount) = await SaveConnectionAsync(databaseContext, stranger.Id, "stable-3");
        var others = await FindCategoryAsync(databaseContext, owner.Id, "Autres");
        var strangerOthers = await FindCategoryAsync(databaseContext, stranger.Id, "Autres");
        var transfer = CreateTransaction(current.Id, others.Id, "e1", "VIR VERS LIVRET", -50_000, new DateOnly(2026, 9, 1));
        transfer.MarkAsInternalTransfer(true);
        await SaveTransactionsAsync(databaseContext,
            transfer,
            CreateTransaction(current.Id, others.Id, "e2", "CB CAFE", -250, new DateOnly(2026, 10, 1)),
            CreateTransaction(savings.Id, others.Id, "e3", "INTERETS", 100, new DateOnly(2026, 11, 1)),
            CreateTransaction(strangerAccount.Id, strangerOthers.Id, "e4", "CB CAFE", -250, new DateOnly(2026, 1, 1)));
        var repository = new TransactionRepository(databaseContext);

        Assert.Equal(new DateOnly(2026, 9, 1), await repository.FindEarliestDateAsync(owner.Id, null));
        Assert.Equal(new DateOnly(2026, 11, 1), await repository.FindEarliestDateAsync(owner.Id, savings.Id));
        Assert.Null(await repository.FindEarliestDateAsync(stranger.Id, current.Id));
        Assert.Equal(new DateOnly(2026, 1, 1), await repository.FindEarliestDateAsync(stranger.Id, null));
    }

    // Deux mises à jour ajoutent la même opération au même instant : la contrainte ux_transaction_externe refuse la seconde,
    // et l'unité de travail y voit un conflit, pas une panne. Une seule opération reste.
    [Fact]
    public async Task SameOperationAddedMeanwhileIsAConflict()
    {
        var email = TestAccountCleanup.CreateUniqueEmail("test-operations-doublon");

        try
        {
            await using var setupContext = CreateDatabaseContext();
            var user = await SaveAccountAsync(setupContext, email);
            var (_, account) = await SaveConnectionAsync(setupContext, user.Id, "stable-1");
            var others = await FindCategoryAsync(setupContext, user.Id, "Autres");
            await using var firstContext = CreateDatabaseContext();
            await using var secondContext = CreateDatabaseContext();
            new TransactionRepository(firstContext).PrepareTransaction(CreateTransaction(account.Id, others.Id, "empreinte:0"));
            new TransactionRepository(secondContext).PrepareTransaction(CreateTransaction(account.Id, others.Id, "empreinte:0"));

            Assert.True(await TrySaveChangesAsync(firstContext));
            Assert.False(await TrySaveChangesAsync(secondContext));

            Assert.Empty(secondContext.ChangeTracker.Entries());
            Assert.Equal(1, await setupContext.BankTransactions.CountAsync(row => row.BankAccountId == account.Id));
        }
        finally
        {
            await TestAccountCleanup.DeleteAccountAsync(email);
        }
    }

    // Une catégorie supprimée pendant l'arrivée d'opérations qui la visent : la clé différée les refuse à la validation,
    // seule ou avec d'autres écritures, et l'unité de travail y voit un conflit. Le passage suivant relira les catégories.
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task CategoryDeletedMeanwhileIsAConflict(int operationCount)
    {
        var email = TestAccountCleanup.CreateUniqueEmail("test-operations-categorie");

        try
        {
            await using var setupContext = CreateDatabaseContext();
            var user = await SaveAccountAsync(setupContext, email);
            var (_, account) = await SaveConnectionAsync(setupContext, user.Id, "stable-1");
            var leisure = await FindCategoryAsync(setupContext, user.Id, "Loisirs");
            await using var importContext = CreateDatabaseContext();
            var repository = new TransactionRepository(importContext);

            for (var number = 0; number < operationCount; number++)
            {
                repository.PrepareTransaction(CreateTransaction(account.Id, leisure.Id, "empreinte:" + number));
            }

            await using var deletionContext = CreateDatabaseContext();
            Assert.True(await new CategoryRepository(deletionContext).DeleteAsync(user.Id, leisure.Id));

            Assert.False(await TrySaveChangesAsync(importContext));
            Assert.Equal(0, await setupContext.BankTransactions.CountAsync(row => row.BankAccountId == account.Id));
        }
        finally
        {
            await TestAccountCleanup.DeleteAccountAsync(email);
        }
    }

    // Une règle créée pour une catégorie qu'une autre requête vient de supprimer : conflit, pas panne.
    [Fact]
    public async Task RuleOfACategoryDeletedMeanwhileIsAConflict()
    {
        var email = TestAccountCleanup.CreateUniqueEmail("test-regles-categorie");

        try
        {
            await using var setupContext = CreateDatabaseContext();
            var user = await SaveAccountAsync(setupContext, email);
            var leisure = await FindCategoryAsync(setupContext, user.Id, "Loisirs");
            await using var ruleContext = CreateDatabaseContext();
            new TransactionRepository(ruleContext).PrepareRule(new CategorizationRule { Label = "CB CARREFOUR", CategoryId = leisure.Id });

            await using var deletionContext = CreateDatabaseContext();
            Assert.True(await new CategoryRepository(deletionContext).DeleteAsync(user.Id, leisure.Id));

            Assert.False(await TrySaveChangesAsync(ruleContext));
            Assert.Equal(0, await setupContext.CategorizationRules.CountAsync(rule => rule.CategoryId == leisure.Id));
        }
        finally
        {
            await TestAccountCleanup.DeleteAccountAsync(email);
        }
    }

    // Des opérations qui arrivent pour un compte dont la banque vient d'être déconnectée : conflit, pas panne.
    // Le passage suivant ne trouvera plus la connexion, et n'ajoutera rien.
    [Fact]
    public async Task AccountDeletedMeanwhileIsAConflict()
    {
        var email = TestAccountCleanup.CreateUniqueEmail("test-operations-compte");

        try
        {
            await using var setupContext = CreateDatabaseContext();
            var user = await SaveAccountAsync(setupContext, email);
            var (connection, account) = await SaveConnectionAsync(setupContext, user.Id, "stable-1");
            var others = await FindCategoryAsync(setupContext, user.Id, "Autres");
            await using var importContext = CreateDatabaseContext();
            new TransactionRepository(importContext).PrepareTransaction(CreateTransaction(account.Id, others.Id, "empreinte:0"));

            await using var deletionContext = CreateDatabaseContext();
            Assert.True(await new BankConnectionRepository(deletionContext).DeleteAsync(user.Id, connection.Id, connection.ExternalConnectionId));

            Assert.False(await TrySaveChangesAsync(importContext));
            Assert.Equal(0, await setupContext.BankTransactions.CountAsync(row => row.BankAccountId == account.Id));
        }
        finally
        {
            await TestAccountCleanup.DeleteAccountAsync(email);
        }
    }
}
