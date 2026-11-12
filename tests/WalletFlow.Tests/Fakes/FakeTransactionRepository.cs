using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les opérations et les règles de classement en mémoire, dans les listes
// du faux dépôt des catégories, qui range les opérations d'une catégorie supprimée dans « Autres ».
// Les comptes et les connexions viennent du faux dépôt des connexions : une opération est à l'utilisateur de sa connexion.
// Ce qui est préparé n'est gardé qu'avec les autres changements (voir FakeDatabaseUnitOfWork).
// La vraie recherche, avec ILIKE et l'ordre de PostgreSQL, est testée sur la vraie base (TransactionRepositoryTests).
public class FakeTransactionRepository : ITransactionRepository, ITransactionalFakeRepository
{
    private readonly object pendingChangesLock = new object();
    private readonly FakeBankConnectionRepository bankRepository;
    private readonly FakeCategoryRepository categoryRepository;
    private readonly List<BankTransaction> preparedTransactions = new List<BankTransaction>();
    private readonly List<CategorizationRule> preparedRules = new List<CategorizationRule>();
    private readonly ReadCopies<BankTransaction> readTransactions = new ReadCopies<BankTransaction>();
    private readonly ReadCopies<CategorizationRule> readRules = new ReadCopies<CategorizationRule>();
    private Action? concurrentChanges = null;

    // Reçoit les faux dépôts des connexions et des catégories, dont les listes forment la même fausse base.
    public FakeTransactionRepository(FakeBankConnectionRepository bankRepository, FakeCategoryRepository categoryRepository)
    {
        this.bankRepository = bankRepository;
        this.categoryRepository = categoryRepository;
    }

    // Ce que la fausse base contient.
    public List<BankTransaction> SavedTransactions => categoryRepository.SavedTransactions;
    public List<CategorizationRule> SavedRules => categoryRepository.SavedRules;

    // Simule d'autres requêtes qui écrivent les mêmes lignes juste avant chaque enregistrement : un conflit chacune.
    public int Conflicts { get; set; } = 0;

    // Simule une autre mise à jour qui ajoute ces opérations juste avant l'enregistrement : comme la contrainte d'unicité
    // de la vraie base, l'enregistrement échoue, et le passage suivant les voit déjà gardées.
    public List<BankTransaction>? TransactionsAddedBeforeSave { get; set; } = null;

    // Simule une autre requête qui crée cette règle juste avant l'enregistrement d'une règle : « Autres » a changé de version.
    public CategorizationRule? RuleCreatedBeforeSave { get; set; } = null;

    // Donne la date de la dernière opération gardée pour ce compte.
    public Task<DateOnly?> FindLatestDateAsync(Guid accountId)
    {
        lock (pendingChangesLock)
        {
            var dates = SavedTransactions
                .Where(transaction => transaction.BankAccountId == accountId)
                .Select(transaction => (DateOnly?)transaction.TransactionDate)
                .ToList();

            return Task.FromResult(dates.Max());
        }
    }

    // Donne les identifiants des opérations du compte à partir de ce jour.
    public Task<HashSet<string>> FindExternalIdsAsync(Guid accountId, DateOnly fromDate)
    {
        lock (pendingChangesLock)
        {
            var externalIds = SavedTransactions
                .Where(transaction => transaction.BankAccountId == accountId && transaction.TransactionDate >= fromDate)
                .Select(transaction => transaction.ExternalTransactionId);

            return Task.FromResult(new HashSet<string>(externalIds));
        }
    }

    // Donne les règles des catégories de l'utilisateur.
    public Task<List<CategorizationRule>> FindRulesAsync(Guid userId)
    {
        lock (pendingChangesLock)
        {
            var rules = SavedRules.Where(rule => IsUserCategory(userId, rule.CategoryId)).ToList();
            rules.ForEach(readRules.Keep);

            return Task.FromResult(rules);
        }
    }

    // Cherche la règle de l'utilisateur pour ce libellé.
    public Task<CategorizationRule?> FindRuleAsync(Guid userId, string ruleLabel)
    {
        lock (pendingChangesLock)
        {
            var rule = SavedRules.FirstOrDefault(saved => saved.Label == ruleLabel && IsUserCategory(userId, saved.CategoryId));

            if (rule != null)
            {
                readRules.Keep(rule);
            }

            return Task.FromResult(rule);
        }
    }

    // Cherche l'opération parmi celles des comptes de l'utilisateur.
    public Task<BankTransaction?> FindAsync(Guid userId, Guid transactionId)
    {
        lock (pendingChangesLock)
        {
            var transaction = SavedTransactions.SingleOrDefault(saved =>
                saved.Id == transactionId
                && IsUserAccount(userId, saved.BankAccountId));

            if (transaction != null)
            {
                readTransactions.Keep(transaction);
            }

            return Task.FromResult(transaction);
        }
    }

    // Donne l'opération de l'utilisateur telle qu'il la voit.
    public Task<TransactionView?> ReadViewAsync(Guid userId, Guid transactionId)
    {
        lock (pendingChangesLock)
        {
            var view = ListViews(userId).SingleOrDefault(saved => saved.Id == transactionId);

            return Task.FromResult(view);
        }
    }

    // Cherche parmi les opérations de l'utilisateur, comme la vraie base : le texte sans les majuscules,
    // le montant sans son signe, les dates comprises, les plus récentes d'abord.
    public Task<TransactionPage> SearchAsync(Guid userId, TransactionSearch search)
    {
        lock (pendingChangesLock)
        {
            var views = ListViews(userId).Where(view =>
                (search.Text == null || view.Label.ToLowerInvariant().Contains(search.Text.ToLowerInvariant()))
                && (search.AmountInCents == null || Math.Abs(view.AmountInCents) == search.AmountInCents)
                && (search.From == null || view.Date >= search.From)
                && (search.To == null || view.Date <= search.To)
                && (search.AccountId == null || view.AccountId == search.AccountId)
                && (search.CategoryId == null || view.CategoryId == search.CategoryId))
                .OrderByDescending(view => view.Date)
                .ThenByDescending(view => view.Id)
                .ToList();
            var items = views.Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToList();

            return Task.FromResult(new TransactionPage(items, views.Count));
        }
    }

    // Additionne les opérations de l'utilisateur par catégorie et par mois, sans les transferts, comme la vraie base.
    public Task<List<CategoryMonthTotal>> SumByCategoryAndMonthAsync(Guid userId, DateOnly from, DateOnly to, Guid? accountId)
    {
        lock (pendingChangesLock)
        {
            var totals = SavedTransactions
                .Where(transaction =>
                    IsUserAccount(userId, transaction.BankAccountId)
                    && !transaction.IsInternalTransfer
                    && transaction.TransactionDate >= from
                    && transaction.TransactionDate <= to
                    && (accountId == null || transaction.BankAccountId == accountId))
                .GroupBy(transaction => (transaction.CategoryId, transaction.TransactionDate.Year, transaction.TransactionDate.Month))
                .Select(group => new CategoryMonthTotal(
                    group.Key.CategoryId,
                    group.Key.Year,
                    group.Key.Month,
                    group.Sum(transaction => (long)transaction.AmountInCents)))
                .ToList();

            return Task.FromResult(totals);
        }
    }

    // Donne la date de la plus ancienne opération de l'utilisateur, ou de ce compte seul.
    public Task<DateOnly?> FindEarliestDateAsync(Guid userId, Guid? accountId)
    {
        lock (pendingChangesLock)
        {
            var dates = SavedTransactions
                .Where(transaction =>
                    IsUserAccount(userId, transaction.BankAccountId)
                    && (accountId == null || transaction.BankAccountId == accountId))
                .Select(transaction => (DateOnly?)transaction.TransactionDate)
                .ToList();

            return Task.FromResult(dates.Min());
        }
    }

    // Garde l'opération de côté : elle ne sera gardée qu'avec les autres changements.
    public void PrepareTransaction(BankTransaction transaction)
    {
        lock (pendingChangesLock)
        {
            preparedTransactions.Add(transaction);
        }
    }

    // Garde la règle de côté : elle ne sera gardée qu'avec les autres changements.
    public void PrepareRule(CategorizationRule rule)
    {
        lock (pendingChangesLock)
        {
            preparedRules.Add(rule);
        }
    }

    // Aucune table de ce faux dépôt ne tombe en panne pendant un enregistrement.
    public void ThrowIfTableIsDown()
    {
    }

    // Décide si une autre requête écrit les mêmes lignes juste avant l'enregistrement, selon les réglages du test.
    public bool HasConcurrentChange()
    {
        lock (pendingChangesLock)
        {
            var changedTransaction = readTransactions.FindChangedSinceRead((copy, current) =>
                copy.CategoryId != current.CategoryId || copy.IsInternalTransfer != current.IsInternalTransfer);
            var changedRule = readRules.FindChangedSinceRead((copy, current) => copy.CategoryId != current.CategoryId);
            var isWriting = preparedTransactions.Count > 0 || preparedRules.Count > 0 || changedTransaction != null || changedRule != null;

            var addedTransactions = TransactionsAddedBeforeSave;
            if (addedTransactions != null && preparedTransactions.Count > 0)
            {
                TransactionsAddedBeforeSave = null;
                concurrentChanges = () => SavedTransactions.AddRange(addedTransactions);
                return true;
            }

            var createdRule = RuleCreatedBeforeSave;
            if (createdRule != null && preparedRules.Count > 0)
            {
                RuleCreatedBeforeSave = null;
                concurrentChanges = () => SavedRules.Add(createdRule);
                return true;
            }

            if (Conflicts > 0 && isWriting)
            {
                Conflicts = Conflicts - 1;
                return true;
            }

            return false;
        }
    }

    // Remet les objets lus comme ils étaient, et oublie ce qui a été préparé.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readTransactions.RestoreAll();
            readRules.RestoreAll();
            ClearTracking();
        }
    }

    // Écrit ce que l'autre requête simulée a enregistré.
    public void ApplyConcurrentChanges()
    {
        Action? changes;

        lock (pendingChangesLock)
        {
            changes = concurrentChanges;
            concurrentChanges = null;
        }

        if (changes != null)
        {
            lock (pendingChangesLock)
            {
                changes();
            }
        }
    }

    // Garde ce qui a été préparé. Comme la contrainte d'unicité de la vraie base, une opération déjà gardée pour le même compte
    // fait échouer l'enregistrement : un cas d'utilisation qui la préparerait deux fois est en faute, et le test le voit.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            foreach (var transaction in preparedTransactions)
            {
                var isDuplicate = SavedTransactions.Any(saved =>
                    saved.BankAccountId == transaction.BankAccountId
                    && saved.ExternalTransactionId == transaction.ExternalTransactionId);

                if (isDuplicate)
                {
                    throw new InvalidOperationException("Une opération déjà gardée pour ce compte a été préparée une seconde fois.");
                }

                SavedTransactions.Add(transaction);
            }

            SavedRules.AddRange(preparedRules);
            ClearTracking();
        }
    }

    // Les opérations de l'utilisateur telles qu'il les voit, avec le nom de leur compte et de leur catégorie.
    private List<TransactionView> ListViews(Guid userId)
    {
        var views = new List<TransactionView>();

        foreach (var transaction in SavedTransactions.Where(saved => IsUserAccount(userId, saved.BankAccountId)))
        {
            var account = bankRepository.SavedAccounts.Single(saved => saved.Id == transaction.BankAccountId);
            var category = categoryRepository.SavedCategories.SingleOrDefault(saved => saved.Id == transaction.CategoryId);

            if (category != null)
            {
                views.Add(new TransactionView(
                    transaction.Id,
                    transaction.TransactionDate,
                    transaction.Label,
                    transaction.AmountInCents,
                    transaction.IsInternalTransfer,
                    account.Id,
                    account.Name,
                    category.Id,
                    category.Name));
            }
        }

        return views;
    }

    // Renvoie vrai si le compte appartient à une connexion de l'utilisateur.
    private bool IsUserAccount(Guid userId, Guid accountId)
    {
        var account = bankRepository.SavedAccounts.FirstOrDefault(saved => saved.Id == accountId);

        return account != null
            && bankRepository.SavedConnections.Any(connection => connection.Id == account.BankConnectionId && connection.UserId == userId);
    }

    // Renvoie vrai si la catégorie appartient à l'utilisateur.
    private bool IsUserCategory(Guid userId, Guid categoryId)
    {
        return categoryRepository.SavedCategories.Any(category => category.Id == categoryId && category.UserId == userId);
    }

    // Oublie ce qui a été lu et préparé depuis le dernier enregistrement.
    private void ClearTracking()
    {
        preparedTransactions.Clear();
        preparedRules.Clear();
        readTransactions.Clear();
        readRules.Clear();
    }
}
