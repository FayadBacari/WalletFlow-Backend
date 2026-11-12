using WalletFlow.Application.Persistence;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la marque de transfert entre deux comptes de l'utilisateur (GR Comptes, SS §4, DD §5.3),
// avec un faux prestataire et une fausse base.
public class TransactionTransferServiceTests
{
    private readonly BankServices services = new BankServices();

    // Connecte deux comptes, chacun avec une opération : un virement de l'un vers l'autre. Renvoie les deux opérations.
    private async Task<(BankTransaction Sent, BankTransaction Received)> ConnectTwoAccountsAsync()
    {
        services.Provider.AddTransaction("stable-1", services.Today, -50_000, "VIR VERS LIVRET A");
        services.Provider.AddTransaction("stable-2", services.Today, 50_000, "VIR DEPUIS COMPTE COURANT");
        var connection = await services.ConnectAsync(
            new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000),
            new TestBankAccount("stable-2", "Livret A", "EUR", 90_000));
        var accounts = services.AccountsOf(connection);

        return (
            services.TransactionsOf(accounts.Single(account => account.Name == "Compte courant")).Single(),
            services.TransactionsOf(accounts.Single(account => account.Name == "Livret A")).Single());
    }

    // GR Comptes : l'utilisateur marque une opération comme transfert, puis peut retirer la marque.
    [Fact]
    public async Task OperationIsMarkedThenUnmarked()
    {
        var (sent, _) = await ConnectTwoAccountsAsync();

        var marked = await services.CreateTransfer().MarkAsync(services.UserId, sent.Id, true);
        Assert.True(Assert.IsType<TransactionTransferResult.Marked>(marked).Transaction.IsInternalTransfer);
        Assert.True(sent.IsInternalTransfer);

        var unmarked = await services.CreateTransfer().MarkAsync(services.UserId, sent.Id, false);
        Assert.False(Assert.IsType<TransactionTransferResult.Marked>(unmarked).Transaction.IsInternalTransfer);
        Assert.False(sent.IsInternalTransfer);
    }

    // Seule l'opération choisie change : l'opération en face, l'utilisateur la marque lui aussi. Sa catégorie ne change pas.
    [Fact]
    public async Task OnlyTheChosenOperationIsMarked()
    {
        var (sent, received) = await ConnectTwoAccountsAsync();
        var categoryBefore = sent.CategoryId;

        await services.CreateTransfer().MarkAsync(services.UserId, sent.Id, true);

        Assert.False(received.IsInternalTransfer);
        Assert.Equal(categoryBefore, sent.CategoryId);
    }

    // SS §4 : l'opération d'un autre utilisateur n'est pas trouvée, et ne change pas.
    [Fact]
    public async Task OperationOfAnotherUserIsNotFound()
    {
        var (sent, _) = await ConnectTwoAccountsAsync();

        var result = await services.CreateTransfer().MarkAsync(Guid.NewGuid(), sent.Id, true);

        Assert.IsType<TransactionTransferResult.NotFound>(result);
        Assert.False(sent.IsInternalTransfer);
    }

    // Si d'autres requêtes changent sans arrêt la même opération, rien n'est enregistré.
    [Fact]
    public async Task ConstantConflictsSaveNothing()
    {
        var (sent, _) = await ConnectTwoAccountsAsync();
        services.Transactions.Conflicts = DatabaseChangesSavingService.MaximumTries;

        var result = await services.CreateTransfer().MarkAsync(services.UserId, sent.Id, true);

        Assert.IsType<TransactionTransferResult.NotSaved>(result);
        Assert.False(sent.IsInternalTransfer);
    }
}
