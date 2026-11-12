using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les opérations bancaires et les règles de classement dans la base PostgreSQL.
// Une opération appartient à l'utilisateur de sa connexion bancaire, et une règle à celui de sa catégorie :
// chaque recherche d'un utilisateur passe par ces liens (SS §4). Les valeurs partent toujours comme des paramètres (SS §5).
public class TransactionRepository : ITransactionRepository
{
    // Le caractère qui protège %, _ et lui-même dans un motif ILIKE : le texte cherché est pris tel quel.
    private const string LikeEscapeCharacter = "\\";

    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public TransactionRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Donne la date de la dernière opération du compte, sans rien suivre.
    public async Task<DateOnly?> FindLatestDateAsync(Guid accountId)
    {
        return await databaseContext.BankTransactions
            .AsNoTracking()
            .Where(transaction => transaction.BankAccountId == accountId)
            .MaxAsync(transaction => (DateOnly?)transaction.TransactionDate);
    }

    // Donne les identifiants des opérations du compte à partir de ce jour, sans rien suivre.
    public async Task<HashSet<string>> FindExternalIdsAsync(Guid accountId, DateOnly fromDate)
    {
        var externalIds = await databaseContext.BankTransactions
            .AsNoTracking()
            .Where(transaction => transaction.BankAccountId == accountId && transaction.TransactionDate >= fromDate)
            .Select(transaction => transaction.ExternalTransactionId)
            .ToListAsync();

        return new HashSet<string>(externalIds);
    }

    // Donne les règles des catégories de l'utilisateur.
    public async Task<List<CategorizationRule>> FindRulesAsync(Guid userId)
    {
        return await databaseContext.CategorizationRules
            .Where(rule => databaseContext.Categories.Any(category => category.Id == rule.CategoryId && category.UserId == userId))
            .ToListAsync();
    }

    // Cherche la règle de l'utilisateur pour ce libellé. S'il y en avait plusieurs, la première par identifiant est toujours la même.
    public async Task<CategorizationRule?> FindRuleAsync(Guid userId, string ruleLabel)
    {
        return await databaseContext.CategorizationRules
            .Where(rule =>
                rule.Label == ruleLabel
                && databaseContext.Categories.Any(category => category.Id == rule.CategoryId && category.UserId == userId))
            .OrderBy(rule => rule.Id)
            .FirstOrDefaultAsync();
    }

    // Cherche l'opération parmi celles des comptes de l'utilisateur : celle d'un autre n'est jamais trouvée.
    public async Task<BankTransaction?> FindAsync(Guid userId, Guid transactionId)
    {
        return await databaseContext.BankTransactions
            .Where(transaction => transaction.Id == transactionId && UserAccountIds(userId).Contains(transaction.BankAccountId))
            .SingleOrDefaultAsync();
    }

    // Donne l'opération telle que l'utilisateur la voit, sans rien suivre.
    public async Task<TransactionView?> ReadViewAsync(Guid userId, Guid transactionId)
    {
        return await QueryRows(userId)
            .Where(row => row.Id == transactionId)
            .Select(row => new TransactionView(
                row.Id, row.Date, row.Label, row.AmountInCents, row.IsInternalTransfer,
                row.AccountId, row.AccountName, row.CategoryId, row.CategoryName))
            .SingleOrDefaultAsync();
    }

    // Applique chaque critère donné, compte les opérations trouvées, puis lit la page demandée,
    // les plus récentes d'abord. À date égale, l'identifiant donne toujours le même ordre d'une page à l'autre.
    public async Task<TransactionPage> SearchAsync(Guid userId, TransactionSearch search)
    {
        var query = QueryRows(userId);

        if (search.Text != null)
        {
            var pattern = "%" + EscapeLikePattern(search.Text) + "%";
            query = query.Where(row => EF.Functions.ILike(row.Label, pattern, LikeEscapeCharacter));
        }

        if (search.AmountInCents is int amountInCents)
        {
            query = query.Where(row => row.AmountInCents == amountInCents || row.AmountInCents == -amountInCents);
        }

        if (search.From is DateOnly from)
        {
            query = query.Where(row => row.Date >= from);
        }

        if (search.To is DateOnly to)
        {
            query = query.Where(row => row.Date <= to);
        }

        if (search.AccountId is Guid accountId)
        {
            query = query.Where(row => row.AccountId == accountId);
        }

        if (search.CategoryId is Guid categoryId)
        {
            query = query.Where(row => row.CategoryId == categoryId);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(row => row.Date)
            .ThenByDescending(row => row.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(row => new TransactionView(
                row.Id, row.Date, row.Label, row.AmountInCents, row.IsInternalTransfer,
                row.AccountId, row.AccountName, row.CategoryId, row.CategoryName))
            .ToListAsync();

        return new TransactionPage(items, totalCount);
    }

    // Additionne en SQL, par catégorie et par mois, les opérations des comptes de l'utilisateur entre ces deux jours compris,
    // sans les transferts. Les montants sont additionnés en bigint : beaucoup d'opérations ne débordent jamais.
    public async Task<List<CategoryMonthTotal>> SumByCategoryAndMonthAsync(Guid userId, DateOnly from, DateOnly to, Guid? accountId)
    {
        var query = databaseContext.BankTransactions
            .AsNoTracking()
            .Where(transaction =>
                UserAccountIds(userId).Contains(transaction.BankAccountId)
                && !transaction.IsInternalTransfer
                && transaction.TransactionDate >= from
                && transaction.TransactionDate <= to);

        if (accountId is Guid onlyAccountId)
        {
            query = query.Where(transaction => transaction.BankAccountId == onlyAccountId);
        }

        return await query
            .GroupBy(transaction => new
            {
                transaction.CategoryId,
                transaction.TransactionDate.Year,
                transaction.TransactionDate.Month
            })
            .Select(group => new CategoryMonthTotal(
                group.Key.CategoryId,
                group.Key.Year,
                group.Key.Month,
                group.Sum(transaction => (long)transaction.AmountInCents)))
            .ToListAsync();
    }

    // Donne la date de la plus ancienne opération des comptes de l'utilisateur, ou de ce compte seul. Un transfert compte aussi :
    // il dit que l'historique remonte jusque-là.
    public async Task<DateOnly?> FindEarliestDateAsync(Guid userId, Guid? accountId)
    {
        var query = databaseContext.BankTransactions
            .AsNoTracking()
            .Where(transaction => UserAccountIds(userId).Contains(transaction.BankAccountId));

        if (accountId is Guid onlyAccountId)
        {
            query = query.Where(transaction => transaction.BankAccountId == onlyAccountId);
        }

        return await query.MinAsync(transaction => (DateOnly?)transaction.TransactionDate);
    }

    // Garde de côté la nouvelle opération : elle ne sera écrite qu'avec les autres changements.
    public void PrepareTransaction(BankTransaction transaction)
    {
        databaseContext.BankTransactions.Add(transaction);
    }

    // Garde de côté la nouvelle règle : elle ne sera écrite qu'avec les autres changements.
    public void PrepareRule(CategorizationRule rule)
    {
        databaseContext.CategorizationRules.Add(rule);
    }

    // Les comptes de l'utilisateur : ceux de ses connexions bancaires.
    private IQueryable<Guid> UserAccountIds(Guid userId)
    {
        return databaseContext.BankAccounts
            .Where(account => databaseContext.BankConnections.Any(connection =>
                connection.Id == account.BankConnectionId
                && connection.UserId == userId))
            .Select(account => account.Id);
    }

    // Les opérations de l'utilisateur, avec le nom de leur compte et de leur catégorie, sans rien suivre.
    // Elles passent par TransactionRow, rempli champ par champ : EF Core sait filtrer et trier sur ses champs,
    // pas sur ceux d'un record rempli par son constructeur.
    private IQueryable<TransactionRow> QueryRows(Guid userId)
    {
        return databaseContext.BankTransactions
            .AsNoTracking()
            .Join(
                databaseContext.BankAccounts,
                transaction => transaction.BankAccountId,
                account => account.Id,
                (transaction, account) => new { Transaction = transaction, Account = account })
            .Join(
                databaseContext.BankConnections,
                row => row.Account.BankConnectionId,
                connection => connection.Id,
                (row, connection) => new { row.Transaction, row.Account, connection.UserId })
            .Join(
                databaseContext.Categories,
                row => row.Transaction.CategoryId,
                category => category.Id,
                (row, category) => new { row.Transaction, row.Account, row.UserId, Category = category })
            .Where(row => row.UserId == userId)
            .Select(row => new TransactionRow
            {
                Id = row.Transaction.Id,
                Date = row.Transaction.TransactionDate,
                Label = row.Transaction.Label,
                AmountInCents = row.Transaction.AmountInCents,
                IsInternalTransfer = row.Transaction.IsInternalTransfer,
                AccountId = row.Account.Id,
                AccountName = row.Account.Name,
                CategoryId = row.Category.Id,
                CategoryName = row.Category.Name
            });
    }

    // Une opération de l'utilisateur et les noms qui l'accompagnent, le temps d'une requête.
    private sealed class TransactionRow
    {
        public Guid Id { get; init; }
        public DateOnly Date { get; init; }
        public string Label { get; init; } = string.Empty;
        public int AmountInCents { get; init; }
        public bool IsInternalTransfer { get; init; }
        public Guid AccountId { get; init; }
        public string AccountName { get; init; } = string.Empty;
        public Guid CategoryId { get; init; }
        public string CategoryName { get; init; } = string.Empty;
    }

    // Protège les caractères spéciaux d'un motif ILIKE : « 100% » cherche le texte « 100% », pas « 100 » suivi de n'importe quoi.
    private static string EscapeLikePattern(string text)
    {
        return text
            .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter)
            .Replace("%", LikeEscapeCharacter + "%")
            .Replace("_", LikeEscapeCharacter + "_");
    }
}
