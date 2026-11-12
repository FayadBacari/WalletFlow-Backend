using WalletFlow.Application.Categories;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Transactions;

// L'arrivée des opérations depuis la banque (GR Comptes et GR Dépenses) : chaque opération nouvelle reçoit sa catégorie,
// et une opération déjà reçue n'est jamais ajoutée deux fois (DD §5.3). Les cas d'utilisation des connexions bancaires
// s'en servent à la connexion, au renouvellement et à chaque mise à jour.
public class BankTransactionArrival
{
    private readonly ITransactionRepository transactionRepository;
    private readonly ICategoryRepository categoryRepository;

    // Reçoit l'accès aux opérations et aux catégories.
    public BankTransactionArrival(ITransactionRepository transactionRepository, ICategoryRepository categoryRepository)
    {
        this.transactionRepository = transactionRepository;
        this.categoryRepository = categoryRepository;
    }

    // Choisit le jour d'où lire les opérations du compte, d'après sa dernière opération gardée (voir TransactionRules).
    public async Task<DateOnly> ChooseReadingStartAsync(Guid accountId, DateOnly today)
    {
        var latestDate = await transactionRepository.FindLatestDateAsync(accountId);

        return TransactionRules.ChooseReadingStart(latestDate, today);
    }

    // Dans le passage en cours de l'appelant, prépare l'ajout des opérations nouvelles de ce compte, chacune dans sa catégorie.
    // Tout est relu à chaque passage : après un conflit, les opérations qu'une autre requête vient d'ajouter sont vues.
    // Renvoie le nombre d'opérations préparées.
    public async Task<int> PrepareNewTransactionsAsync(Guid userId, Guid accountId, List<ReceivedTransaction> receivedTransactions)
    {
        if (receivedTransactions.Count == 0)
        {
            return 0;
        }

        var earliestDate = receivedTransactions.Min(transaction => transaction.Date);
        var knownIds = await transactionRepository.FindExternalIdsAsync(accountId, earliestDate);
        var categories = await categoryRepository.FindAllAsync(userId);
        var rules = await transactionRepository.FindRulesAsync(userId);
        var preparedCount = 0;

        foreach (var received in receivedTransactions)
        {
            if (knownIds.Contains(received.ExternalId))
            {
                continue;
            }

            var categoryId = CategoryChoice.ForArrivingTransaction(received.Label, rules, categories);

            // Le compte de l'utilisateur vient d'être effacé : rien n'est ajouté.
            if (categoryId == null)
            {
                return preparedCount;
            }

            transactionRepository.PrepareTransaction(new BankTransaction
            {
                ExternalTransactionId = received.ExternalId,
                TransactionDate = received.Date,
                Label = received.Label,
                AmountInCents = received.AmountInCents,
                BankAccountId = accountId,
                CategoryId = categoryId.Value
            });
            knownIds.Add(received.ExternalId);
            preparedCount = preparedCount + 1;
        }

        return preparedCount;
    }
}
