using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la recherche d'opérations (GR Comptes, SS §4) : les contrôles des critères, et ce que chaque critère garde.
// La vraie requête SQL est vérifiée sur la vraie base (TransactionRepositoryTests).
public class TransactionSearchServiceTests
{
    private readonly BankServices services = new BankServices();

    // Cherche avec ces critères pour l'utilisateur des tests.
    private async Task<TransactionSearchResult> SearchAsync(
        string? text = null,
        int? amountInCents = null,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? accountId = null,
        Guid? categoryId = null,
        int page = 1)
    {
        return await services.CreateSearch().SearchAsync(services.UserId, text, amountInCents, from, to, accountId, categoryId, page);
    }

    // Donne les libellés trouvés, dans l'ordre de la réponse.
    private static List<string> LabelsOf(TransactionSearchResult result)
    {
        return Assert.IsType<TransactionSearchResult.Found>(result).Page.Items.Select(item => item.Label).ToList();
    }

    // Connecte un compte courant et un livret avec quelques opérations.
    private async Task<List<BankAccount>> ConnectAsync()
    {
        services.Provider.AddTransaction("stable-1", services.Today.AddDays(-3), -1250, "CB CARREFOUR");
        services.Provider.AddTransaction("stable-1", services.Today.AddDays(-2), 1250, "REMBOURSEMENT CARREFOUR");
        services.Provider.AddTransaction("stable-1", services.Today.AddDays(-1), -4000, "CB SNCF");
        services.Provider.AddTransaction("stable-2", services.Today, 10_000, "INTERETS LIVRET");
        var connection = await services.ConnectAsync(
            new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000),
            new TestBankAccount("stable-2", "Livret A", "EUR", 90_000));

        return services.AccountsOf(connection);
    }

    // Sans critère, toutes les opérations de l'utilisateur viennent, les plus récentes d'abord.
    [Fact]
    public async Task WithoutCriteriaEveryOperationComesNewestFirst()
    {
        await ConnectAsync();

        var result = await SearchAsync();

        Assert.Equal(new List<string> { "INTERETS LIVRET", "CB SNCF", "REMBOURSEMENT CARREFOUR", "CB CARREFOUR" }, LabelsOf(result));
        Assert.Equal(4, Assert.IsType<TransactionSearchResult.Found>(result).Page.TotalCount);
    }

    // GR Comptes : par libellé sans les majuscules, par montant sans son signe, par dates comprises.
    [Fact]
    public async Task EachCriterionKeepsItsOperations()
    {
        await ConnectAsync();

        Assert.Equal(new List<string> { "REMBOURSEMENT CARREFOUR", "CB CARREFOUR" }, LabelsOf(await SearchAsync(text: "  carrefour ")));
        Assert.Equal(new List<string> { "REMBOURSEMENT CARREFOUR", "CB CARREFOUR" }, LabelsOf(await SearchAsync(amountInCents: 1250)));
        Assert.Equal(new List<string> { "CB SNCF", "REMBOURSEMENT CARREFOUR" },
            LabelsOf(await SearchAsync(from: services.Today.AddDays(-2), to: services.Today.AddDays(-1))));
        Assert.Equal(new List<string> { "CB CARREFOUR" }, LabelsOf(await SearchAsync(text: "CB", amountInCents: 1250)));
    }

    // Les filtres par compte et par catégorie gardent seulement leurs opérations.
    [Fact]
    public async Task AccountAndCategoryFiltersKeepTheirOperations()
    {
        var accounts = await ConnectAsync();
        var savings = accounts.Single(account => account.Name == "Livret A");

        Assert.Equal(new List<string> { "INTERETS LIVRET" }, LabelsOf(await SearchAsync(accountId: savings.Id)));
        Assert.Equal(new List<string> { "CB SNCF" }, LabelsOf(await SearchAsync(categoryId: services.CategoryNamed("Transport").Id)));
    }

    // SS §4 : les opérations d'un autre utilisateur n'apparaissent jamais.
    [Fact]
    public async Task OperationsOfAnotherUserNeverAppear()
    {
        await ConnectAsync();

        var result = await services.CreateSearch().SearchAsync(Guid.NewGuid(), null, null, null, null, null, null, 1);

        Assert.Empty(Assert.IsType<TransactionSearchResult.Found>(result).Page.Items);
    }

    // 50 opérations par page : la seconde page donne la suite, et le total compte toutes les opérations trouvées.
    [Fact]
    public async Task PagesHoldFiftyOperations()
    {
        for (var day = 1; day <= 51; day++)
        {
            services.Provider.AddTransaction("stable-1", services.Today.AddDays(-day), -100 * day, "CB CAFE " + day);
        }

        await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000));

        var firstPage = Assert.IsType<TransactionSearchResult.Found>(await SearchAsync()).Page;
        var secondPage = Assert.IsType<TransactionSearchResult.Found>(await SearchAsync(page: 2)).Page;

        Assert.Equal(50, firstPage.Items.Count);
        Assert.Equal("CB CAFE 51", Assert.Single(secondPage.Items).Label);
        Assert.Equal(51, secondPage.TotalCount);
    }

    // Le texte cherché tient dans un libellé : 255 caractères au plus, comptés comme PostgreSQL.
    [Fact]
    public async Task TextLongerThanALabelIsRefused()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);

        Assert.IsType<TransactionSearchResult.Found>(await SearchAsync(text: string.Concat(Enumerable.Repeat(emoji, 255))));
        Assert.IsType<TransactionSearchResult.TextTooLong>(await SearchAsync(text: string.Concat(Enumerable.Repeat(emoji, 256))));
    }

    // Le montant cherché est celui d'une opération : de 1 centime à 1 000 000 000 centimes, sans signe.
    [Theory]
    [InlineData(0)]
    [InlineData(-1250)]
    [InlineData(1_000_000_001)]
    public async Task AmountOutsideTheLimitsIsRefused(int amountInCents)
    {
        Assert.IsType<TransactionSearchResult.AmountBreaksRules>(await SearchAsync(amountInCents: amountInCents));
    }

    [Fact]
    public async Task ReversedDatesAreRefused()
    {
        Assert.IsType<TransactionSearchResult.DatesReversed>(await SearchAsync(from: services.Today, to: services.Today.AddDays(-1)));
        Assert.IsType<TransactionSearchResult.Found>(await SearchAsync(from: services.Today, to: services.Today));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public async Task PageOutsideTheLimitsIsRefused(int page)
    {
        Assert.IsType<TransactionSearchResult.PageBreaksRules>(await SearchAsync(page: page));
    }
}
