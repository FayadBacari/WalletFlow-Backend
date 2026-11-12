using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Écrit dans la vraie base les lignes dont les tests des banques et des opérations ont besoin, puis oublie les objets gardés
// en mémoire : chaque test relit ensuite la base, comme une nouvelle requête.
public static class BankTestRows
{
    public static readonly DateTime Now = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    // Enregistre un compte confirmé avec ses catégories par défaut.
    public static async Task<User> SaveAccountAsync(WalletFlowDatabaseContext databaseContext, string email)
    {
        var account = await SaveConfirmedAccountAsync(databaseContext, email);
        databaseContext.Categories.AddRange(DefaultCategories.CreateForUser(account.Id));
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return account;
    }

    // Enregistre une connexion de ce compte, avec un compte bancaire de cet identifiant stable.
    public static async Task<(BankConnection Connection, BankAccount Account)> SaveConnectionAsync(
        WalletFlowDatabaseContext databaseContext,
        Guid userId,
        string stableId,
        BankConnectionStatus status = BankConnectionStatus.Active)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = "session-" + Guid.NewGuid(),
            BankName = "Banque de test",
            Status = status,
            AuthorizationExpiresAt = Now.AddDays(100),
            UserId = userId
        };
        var account = new BankAccount
        {
            ExternalAccountId = stableId,
            Name = "Compte courant",
            BalanceInCents = 10_000,
            BalanceUpdatedAt = Now,
            BankConnectionId = connection.Id
        };
        databaseContext.AddRange(connection, account);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return (connection, account);
    }

    // Crée une opération de ce compte et de cette catégorie, sans l'enregistrer.
    public static BankTransaction CreateTransaction(
        Guid accountId,
        Guid categoryId,
        string externalId,
        string label = "CB CARREFOUR",
        int amountInCents = -1250,
        DateOnly? date = null)
    {
        return new BankTransaction
        {
            ExternalTransactionId = externalId,
            TransactionDate = date ?? DateOnly.FromDateTime(Now),
            Label = label,
            AmountInCents = amountInCents,
            BankAccountId = accountId,
            CategoryId = categoryId
        };
    }

    // Enregistre ces opérations.
    public static async Task SaveTransactionsAsync(WalletFlowDatabaseContext databaseContext, params BankTransaction[] transactions)
    {
        databaseContext.BankTransactions.AddRange(transactions);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
    }
}
