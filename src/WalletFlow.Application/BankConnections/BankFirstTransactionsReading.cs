using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.BankConnections;

// La première lecture des opérations d'une connexion, juste après son autorisation ou son renouvellement (GR Comptes) :
// la fin de la connexion (BankConnectionFinishService) s'en sert, une fois la connexion enregistrée. Quand des opérations arrivent,
// les seuils de budget de l'utilisateur sont vérifiés juste après : seules celles du mois en cours peuvent en déclencher un.
public class BankFirstTransactionsReading
{
    private readonly IBankDataProvider bankProvider;
    private readonly IBankConnectionRepository connectionRepository;
    private readonly BankTransactionArrival transactionArrival;
    private readonly BudgetAlertService budgetAlerts;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit le prestataire, l'accès aux connexions, l'arrivée des opérations, la vérification des budgets,
    // l'enregistrement avec réessai et l'horloge.
    public BankFirstTransactionsReading(
        IBankDataProvider bankProvider,
        IBankConnectionRepository connectionRepository,
        BankTransactionArrival transactionArrival,
        BudgetAlertService budgetAlerts,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.bankProvider = bankProvider;
        this.connectionRepository = connectionRepository;
        this.transactionArrival = transactionArrival;
        this.budgetAlerts = budgetAlerts;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Lit les opérations de chaque compte gardé, l'utilisateur étant là, juste après l'autorisation : c'est alors que la banque
    // donne le plus d'historique. Si elle ne répond pas, la connexion reste faite : les opérations arriveront à la mise à jour
    // suivante, qui repart de la dernière opération gardée.
    public async Task ReadAsync(Guid userId, Guid connectionId, ProvidedSession session, UserPresence presence, CancellationToken cancellationToken = default)
    {
        var today = UsersCalendar.FindToday(clock.GetUtcNow());
        var storedAccounts = await connectionRepository.FindAccountsAsync(new List<Guid> { connectionId }, cancellationToken);
        var transactionsByAccount = new Dictionary<Guid, List<ReceivedTransaction>>();

        foreach (var storedAccount in storedAccounts)
        {
            var sessionAccount = session.Accounts.FirstOrDefault(account => account.StableId == storedAccount.ExternalAccountId);

            if (sessionAccount == null)
            {
                continue;
            }

            var startDate = await transactionArrival.ChooseReadingStartAsync(storedAccount.Id, today, cancellationToken);

            try
            {
                var providedTransactions = await bankProvider.ReadTransactionsAsync(sessionAccount.Uid, startDate, presence, cancellationToken);
                transactionsByAccount[storedAccount.Id] = TransactionReception.Receive(providedTransactions);
            }
            catch (BankProviderException)
            {
            }
        }

        if (transactionsByAccount.Count == 0)
        {
            return;
        }

        var addedCount = await savingService.SaveWithRetriesAsync(
            () => PrepareFirstTransactionsAsync(userId, connectionId, transactionsByAccount, cancellationToken),
            0,
            cancellationToken);

        if (addedCount > 0)
        {
            await budgetAlerts.CheckAsync(userId, cancellationToken);
        }
    }

    // Un passage : vérifie que la connexion est toujours là, puis prépare les opérations nouvelles de chacun de ses comptes.
    private async Task<PreparedChanges<int>> PrepareFirstTransactionsAsync(
        Guid userId,
        Guid connectionId,
        Dictionary<Guid, List<ReceivedTransaction>> transactionsByAccount,
        CancellationToken cancellationToken = default)
    {
        var connection = await connectionRepository.FindConnectionAsync(userId, connectionId, cancellationToken);

        if (connection == null)
        {
            return PreparedChanges<int>.NothingToSave(0);
        }

        var accounts = await connectionRepository.FindAccountsAsync(new List<Guid> { connectionId }, cancellationToken);
        var preparedCount = 0;

        foreach (var account in accounts)
        {
            if (transactionsByAccount.TryGetValue(account.Id, out var receivedTransactions))
            {
                preparedCount = preparedCount + await transactionArrival.PrepareNewTransactionsAsync(userId, account.Id, receivedTransactions, cancellationToken);
            }
        }

        if (preparedCount == 0)
        {
            return PreparedChanges<int>.NothingToSave(0);
        }

        return PreparedChanges<int>.SaveThenReturn(preparedCount);
    }
}
