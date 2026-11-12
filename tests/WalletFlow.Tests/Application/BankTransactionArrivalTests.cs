using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie l'arrivée des opérations depuis la banque : à la connexion, au renouvellement et à chaque mise à jour
// (GR Comptes et GR Dépenses, DD §5.3 et §6.2), avec un faux prestataire et une fausse base.
public class BankTransactionArrivalTests
{
    private const string StableId = "stable-1";

    private readonly BankServices services = new BankServices();

    // Connecte la banque des tests avec un compte courant, et renvoie ce compte.
    private async Task<BankAccount> ConnectAsync()
    {
        var connection = await services.ConnectAsync(new TestBankAccount(StableId, "Compte courant", "EUR", 10_000));

        return services.AccountsOf(connection).Single();
    }

    // Demande une mise à jour, l'utilisateur étant là, une heure plus tard.
    private async Task UpdateLaterAsync()
    {
        services.Clock.Advance(TimeSpan.FromHours(1));
        await services.CreateUpdate().UpdateForUserAsync(services.UserId, services.Presence);
    }

    // À la connexion, un an d'opérations est lu, l'utilisateur étant là, et chacune reçoit sa catégorie (GR Dépenses).
    [Fact]
    public async Task ConnectionReadsOneYearOfOperationsInTheirCategories()
    {
        services.Provider.AddTransaction(StableId, services.Today.AddDays(-3), -4520, "CB CARREFOUR MARKET 01/11");
        services.Provider.AddTransaction(StableId, services.Today.AddDays(-2), 250_000, "VIR SEPA SALAIRE OCTOBRE");
        services.Provider.AddTransaction(StableId, services.Today.AddDays(-1), -1500, "VIR M DUPONT");

        var account = await ConnectAsync();

        var reading = Assert.Single(services.Provider.TransactionReadings);
        Assert.Equal(services.Today.AddDays(-365), reading.StartDate);
        Assert.Equal(services.Presence, reading.Presence);
        var transactions = services.TransactionsOf(account);
        Assert.Equal(3, transactions.Count);
        Assert.Equal(services.CategoryNamed("Alimentation").Id, transactions[0].CategoryId);
        Assert.Equal(services.CategoryNamed("Revenus").Id, transactions[1].CategoryId);
        Assert.Equal(services.CategoryNamed("Autres").Id, transactions[2].CategoryId);
        Assert.Equal(-4520, transactions[0].AmountInCents);
        Assert.Equal(services.Today.AddDays(-3), transactions[0].TransactionDate);
        Assert.Equal("CB CARREFOUR MARKET 01/11", transactions[0].Label);
        Assert.False(transactions[0].IsInternalTransfer);
    }

    // GR Comptes : une opération en attente n'arrive pas ; elle arrive dès que la banque l'a comptabilisée.
    [Fact]
    public async Task PendingOperationArrivesOnceBooked()
    {
        var pending = new ProvidedTransaction("REF-1", false, services.Today, -1200, "EUR", "CB CINEMA");
        services.Provider.AddProvidedTransaction(StableId, pending);
        var account = await ConnectAsync();
        Assert.Empty(services.TransactionsOf(account));

        services.Provider.TransactionsByStableId[StableId][0] = pending with { IsBooked = true };
        await UpdateLaterAsync();

        Assert.Equal(services.CategoryNamed("Loisirs").Id, Assert.Single(services.TransactionsOf(account)).CategoryId);
    }

    // Si la banque ne donne pas les opérations à la connexion, la connexion reste faite : elles arrivent à la mise à jour suivante,
    // qui lit toujours un an, puisque le compte n'a encore aucune opération.
    [Fact]
    public async Task ConnectionStaysWhenOperationsCannotBeRead()
    {
        services.Provider.AddTransaction(StableId, services.Today.AddDays(-1), -1000, "CB CARREFOUR");
        services.Provider.TransactionFailure = BankProviderFailure.Unavailable;
        var account = await ConnectAsync();
        Assert.Empty(services.TransactionsOf(account));

        services.Provider.TransactionFailure = null;
        await UpdateLaterAsync();

        Assert.Single(services.TransactionsOf(account));
        Assert.Equal(services.Today.AddDays(-365), services.Provider.TransactionReadings.Last().StartDate);
    }

    // DD §5.3 : une mise à jour repart de la dernière opération gardée moins 7 jours, et n'ajoute jamais deux fois une opération.
    [Fact]
    public async Task UpdateReadsFromAWeekBeforeTheLatestOperationWithoutDuplicates()
    {
        services.Provider.AddTransaction(StableId, services.Today.AddDays(-10), -1000, "CB CARREFOUR");
        services.Provider.AddTransaction(StableId, services.Today.AddDays(-2), -2000, "CB SNCF");
        var account = await ConnectAsync();
        services.Provider.AddTransaction(StableId, services.Today, -3000, "CB NETFLIX");

        await UpdateLaterAsync();
        await UpdateLaterAsync();

        Assert.Equal(services.Today.AddDays(-7), services.Provider.TransactionReadings.Last().StartDate);
        Assert.Equal(services.Presence, services.Provider.TransactionReadings.Last().Presence);
        var transactions = services.TransactionsOf(account);
        Assert.Equal(3, transactions.Count);
        Assert.Equal(3, transactions.Select(transaction => transaction.ExternalTransactionId).Distinct().Count());
    }

    // Un prélèvement mensuel, avec la même référence et le même montant, arrive chaque mois, à chaque mise à jour.
    // La fausse base refuse un identifiant déjà gardé, comme la contrainte d'unicité de la vraie.
    [Fact]
    public async Task MonthlyOperationArrivesEveryMonth()
    {
        services.Provider.AddTransaction(StableId, services.Today.AddDays(-30), -1399, "PRLV NETFLIX", "MANDAT-1");
        var account = await ConnectAsync();
        services.Provider.AddTransaction(StableId, services.Today, -1399, "PRLV NETFLIX", "MANDAT-1");

        await UpdateLaterAsync();

        Assert.Equal(2, services.TransactionsOf(account).Count);
    }

    // GR Comptes : en arrière-plan, l'utilisateur n'est pas là : aucune présence n'est transmise pour les opérations.
    [Fact]
    public async Task BackgroundUpdateReadsOperationsWithoutPresence()
    {
        var account = await ConnectAsync();
        services.Provider.AddTransaction(StableId, services.Today, -3000, "CB NETFLIX");
        services.Clock.Advance(TimeSpan.FromHours(1));

        await services.CreateBackgroundUpdate().UpdateDueConnectionsAsync();

        Assert.Null(services.Provider.TransactionReadings.Last().Presence);
        Assert.Single(services.TransactionsOf(account));
    }

    // Si la banque refuse les opérations, le solde déjà lu est quand même enregistré : seules les opérations attendent.
    [Theory]
    [InlineData(BankProviderFailure.Unavailable)]
    [InlineData(BankProviderFailure.RateLimited)]
    public async Task RefusedOperationsDoNotBlockTheBalance(BankProviderFailure failure)
    {
        var account = await ConnectAsync();
        services.Provider.ChangeBalance(StableId, 42_000);
        services.Provider.AddTransaction(StableId, services.Today, -3000, "CB NETFLIX");
        services.Provider.TransactionFailure = failure;

        await UpdateLaterAsync();

        Assert.Equal(42_000, account.BalanceInCents);
        Assert.Empty(services.TransactionsOf(account));
        Assert.Equal(BankConnectionStatus.Active, services.Repository.SavedConnections.Single().Status);
    }

    // Une session finie pendant la lecture des opérations fait expirer la connexion, comme pendant celle des soldes.
    [Fact]
    public async Task EndedSessionDuringOperationsExpiresTheConnection()
    {
        await ConnectAsync();
        services.Provider.TransactionFailure = BankProviderFailure.SessionEnded;

        await UpdateLaterAsync();

        Assert.Equal(BankConnectionStatus.Expired, services.Repository.SavedConnections.Single().Status);
    }

    // GR Dépenses : la règle créée par l'utilisateur classe les prochaines opérations de même libellé, avant la règle commune.
    [Fact]
    public async Task UserRuleClassifiesTheNextOperations()
    {
        services.Provider.AddTransaction(StableId, services.Today.AddDays(-30), -4500, "CB CARREFOUR 05/10");
        var account = await ConnectAsync();
        var leisure = services.CategoryNamed("Loisirs");
        var change = await services.CreateCategoryChange().ChangeAsync(services.UserId, services.TransactionsOf(account).Single().Id, leisure.Id, true);
        Assert.IsType<TransactionCategoryChangeResult.Changed>(change);

        services.Provider.AddTransaction(StableId, services.Today, -3900, "cb Carrefour 04/11");
        await UpdateLaterAsync();

        var transactions = services.TransactionsOf(account);
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, transaction => Assert.Equal(leisure.Id, transaction.CategoryId));
    }

    // Une autre mise à jour ajoute la même opération au même instant : l'enregistrement échoue, relit, et ne l'ajoute pas deux fois.
    // La fausse base refuse un doublon comme la contrainte d'unicité de la vraie.
    [Fact]
    public async Task OperationAddedMeanwhileIsNotAddedTwice()
    {
        var account = await ConnectAsync();
        services.Provider.ChangeBalance(StableId, 42_000);
        services.Provider.AddTransaction(StableId, services.Today, -3000, "CB NETFLIX");
        var received = TransactionReception.Receive(services.Provider.TransactionsByStableId[StableId]).Single();
        var addedByTheOtherRequest = new BankTransaction
        {
            ExternalTransactionId = received.ExternalId,
            TransactionDate = received.Date,
            Label = received.Label,
            AmountInCents = received.AmountInCents,
            BankAccountId = account.Id,
            CategoryId = services.CategoryNamed("Shopping").Id
        };
        services.Transactions.TransactionsAddedBeforeSave = new List<BankTransaction> { addedByTheOtherRequest };

        await UpdateLaterAsync();

        Assert.Same(addedByTheOtherRequest, Assert.Single(services.TransactionsOf(account)));
        Assert.Equal(42_000, account.BalanceInCents);
    }

    // Au renouvellement, les opérations sont lues avec la nouvelle session, depuis la dernière gardée moins 7 jours, sans doublon.
    [Fact]
    public async Task RenewalReadsTheOperationsWithTheNewSession()
    {
        services.Provider.AddTransaction(StableId, services.Today.AddDays(-5), -1000, "CB CARREFOUR");
        var account = await ConnectAsync();
        var connection = services.Repository.SavedConnections.Single();
        services.Provider.AddTransaction(StableId, services.Today, -2000, "CB SNCF");

        var start = await services.CreateStart().StartRenewalAsync(services.UserId, connection.Id);
        Assert.IsType<BankAuthorizationStartResult.Started>(start);
        services.Provider.AddSession("code-renouvellement", new TestBankAccount(StableId, "Compte courant", "EUR", 10_000));
        var result = await services.CreateFinish().FinishAsync(
            services.UserId, "code-renouvellement", services.Provider.AuthorizationRequests.Last().State, services.Presence);

        Assert.IsType<BankConnectionFinishResult.Renewed>(result);
        Assert.Equal(services.Today.AddDays(-12), services.Provider.TransactionReadings.Last().StartDate);
        Assert.Equal(services.Presence, services.Provider.TransactionReadings.Last().Presence);
        Assert.Equal(2, services.TransactionsOf(account).Count);
    }

    // Un compte dont l'utilisateur vient d'être effacé ne reçoit rien : il n'a plus de catégorie où ranger les opérations.
    [Fact]
    public async Task NothingArrivesForAnErasedUser()
    {
        var account = await ConnectAsync();
        services.Categories.SavedCategories.RemoveAll(category => category.UserId == services.UserId);
        services.Provider.AddTransaction(StableId, services.Today, -3000, "CB NETFLIX");

        await UpdateLaterAsync();

        Assert.Empty(services.TransactionsOf(account));
    }
}
