using System.Net;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie que tout ce qui change les dépenses du mois vérifie les budgets juste après (GR Dépenses, MPD §3) : opérations
// reçues de la banque, catégorie changée, transfert marqué ou non, banque déconnectée. Le budget et les catégories ont
// leurs propres cas dans BudgetAlertServiceTests. L'horloge des tests marque le 4 novembre 2026.
public class BudgetAlertTriggersTests
{
    private static readonly DateOnly November3 = new DateOnly(2026, 11, 3);

    private readonly BankServices services = new BankServices();

    // Donne un budget de 100,00 € à « Loisirs », et à « Autres », où va une opération qu'aucune règle ne reconnaît.
    public BudgetAlertTriggersTests()
    {
        services.CategoryNamed("Loisirs").ReplaceMonthlyBudget(10_000);
        services.CategoryNamed("Autres").ReplaceMonthlyBudget(10_000);
    }

    // Les alertes de budget gardées pour l'utilisateur.
    private List<Alert> BudgetAlerts()
    {
        return services.Alerts.AlertsOf(services.UserId).Where(alert => alert.Kind == AlertKind.BudgetReached).ToList();
    }

    // Connecte la banque des tests avec un compte, en lui donnant d'abord ces opérations.
    private async Task<BankConnection> ConnectWithTransactionsAsync(string stableId, params (DateOnly Date, long AmountInCents)[] transactions)
    {
        foreach (var transaction in transactions)
        {
            services.Provider.AddTransaction(stableId, transaction.Date, transaction.AmountInCents, "OPERATION SANS REGLE " + Guid.NewGuid().ToString("N"));
        }

        return await services.ConnectAsync(new TestBankAccount(stableId, "Compte courant", "EUR", 100_000));
    }

    // GR Comptes : à la connexion, jusqu'à un an d'opérations arrivent. Seules celles du mois en cours comptent : 90 € en
    // novembre envoient l'alerte des 80 %, et les 500 € de mars n'envoient rien, ni pour mars ni pour novembre.
    [Fact]
    public async Task FirstReadingChecksOnlyTheCurrentMonth()
    {
        await ConnectWithTransactionsAsync(
            "stable-1",
            (new DateOnly(2026, 3, 10), -50_000),
            (November3, -9_000));

        var alert = Assert.Single(BudgetAlerts());
        Assert.Equal(services.CategoryNamed("Autres").Id, alert.CategoryId);
        Assert.Equal(new DateOnly(2026, 11, 1), alert.Month);
        Assert.Equal((short)80, alert.Threshold);
    }

    // GR Comptes : à chaque mise à jour, les nouvelles opérations arrivent, puis les budgets sont vérifiés.
    [Fact]
    public async Task UpdateWithNewTransactionsChecksTheBudgets()
    {
        var connection = await ConnectWithTransactionsAsync("stable-1", (November3, -5_000));
        Assert.Empty(BudgetAlerts());
        services.Provider.AddTransaction("stable-1", new DateOnly(2026, 11, 4), -6_000, "OPERATION SANS REGLE NOUVELLE");
        services.Clock.Advance(TimeSpan.FromHours(7));

        await services.CreateUpdate().UpdateConnectionAsync(connection.Id, null);

        var alerts = BudgetAlerts().OrderBy(alert => alert.Threshold).ToList();
        Assert.Equal(new List<short?> { 80, 100 }, alerts.Select(alert => alert.Threshold).ToList());
        Assert.Single(services.EmailRequests.SavedRequests, request => request.Type == EmailRequestType.Alert);
    }

    // Changer la catégorie d'une opération change les dépenses de sa nouvelle catégorie.
    [Fact]
    public async Task CategoryChangeChecksTheNewCategory()
    {
        var connection = await ConnectWithTransactionsAsync("stable-1");
        var account = services.AccountsOf(connection).Single();
        var transaction = services.SaveTransaction(account, "Alimentation", November3, -9_000);

        var result = await services.CreateCategoryChange().ChangeAsync(services.UserId, transaction.Id, services.CategoryNamed("Loisirs").Id, false);

        Assert.IsType<TransactionCategoryChangeResult.Changed>(result);
        Assert.Equal(services.CategoryNamed("Loisirs").Id, Assert.Single(BudgetAlerts()).CategoryId);
    }

    // GR Comptes : un transfert ne compte pas. Marquer un remboursement comme transfert fait donc grandir la dépense,
    // et retirer la marque d'une dépense aussi.
    [Fact]
    public async Task TransferMarkChecksTheBudgets()
    {
        var connection = await ConnectWithTransactionsAsync("stable-1");
        var account = services.AccountsOf(connection).Single();
        services.SaveTransaction(account, "Loisirs", November3, -9_000);
        var refund = services.SaveTransaction(account, "Loisirs", November3, 2_000);
        var expenseMarkedAsTransfer = services.SaveTransaction(account, "Autres", November3, -9_500, isInternalTransfer: true);

        await services.CreateTransfer().MarkAsync(services.UserId, refund.Id, true);
        Assert.Equal(services.CategoryNamed("Loisirs").Id, Assert.Single(BudgetAlerts()).CategoryId);

        await services.CreateTransfer().MarkAsync(services.UserId, expenseMarkedAsTransfer.Id, false);

        Assert.Contains(BudgetAlerts(), alert => alert.CategoryId == services.CategoryNamed("Autres").Id);
    }

    // Déconnecter une banque retire ses opérations : sans le remboursement qu'elle portait, la dépense grandit.
    [Fact]
    public async Task DisconnectionThatRemovesARefundChecksTheBudgets()
    {
        var spendingConnection = await ConnectWithTransactionsAsync("stable-1");
        services.SaveTransaction(services.AccountsOf(spendingConnection).Single(), "Loisirs", November3, -9_000);
        var refundConnection = new BankConnection
        {
            ExternalConnectionId = "session-remboursement",
            BankName = "Autre banque",
            AuthorizationExpiresAt = services.Now.AddDays(100),
            UserId = services.UserId
        };
        var refundAccount = new BankAccount
        {
            ExternalAccountId = "stable-2",
            Name = "Livret",
            BalanceInCents = 0,
            BalanceUpdatedAt = services.Now,
            BankConnectionId = refundConnection.Id
        };
        services.Repository.SavedConnections.Add(refundConnection);
        services.Repository.SavedAccounts.Add(refundAccount);
        services.SaveTransaction(refundAccount, "Loisirs", November3, 2_000);

        var result = await services.CreateDisconnection().DisconnectAsync(services.UserId, refundConnection.Id, IPAddress.Loopback);

        Assert.IsType<BankDisconnectionResult.Disconnected>(result);
        Assert.Equal(services.CategoryNamed("Loisirs").Id, Assert.Single(BudgetAlerts()).CategoryId);
    }

    // Un changement refusé ne change aucune dépense : aucune alerte ne part.
    [Fact]
    public async Task RefusedChangeSendsNoAlert()
    {
        var connection = await ConnectWithTransactionsAsync("stable-1");
        services.SaveTransaction(services.AccountsOf(connection).Single(), "Loisirs", November3, -9_000);

        var result = await services.CreateTransfer().MarkAsync(services.UserId, Guid.NewGuid(), true);

        Assert.IsType<TransactionTransferResult.NotFound>(result);
        Assert.Empty(BudgetAlerts());
    }
}
