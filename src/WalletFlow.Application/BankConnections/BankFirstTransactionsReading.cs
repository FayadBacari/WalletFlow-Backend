using WalletFlow.Application.Persistence;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.BankConnections;

// La première lecture des opérations d'une connexion, juste après son autorisation ou son renouvellement (GR Comptes) :
// la fin de la connexion (BankConnectionFinishService) s'en sert, une fois la connexion enregistrée.
public class BankFirstTransactionsReading
{
    private readonly IBankDataProvider bankProvider;
    private readonly IBankConnectionRepository connectionRepository;
    private readonly BankTransactionArrival transactionArrival;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit le prestataire, l'accès aux connexions, l'arrivée des opérations, l'enregistrement avec réessai et l'horloge.
    public BankFirstTransactionsReading(
        IBankDataProvider bankProvider,
        IBankConnectionRepository connectionRepository,
        BankTransactionArrival transactionArrival,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.bankProvider = bankProvider;
        this.connectionRepository = connectionRepository;
        this.transactionArrival = transactionArrival;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Lit les opérations de chaque compte gardé, l'utilisateur étant là, juste après l'autorisation : c'est alors que la banque
    // donne le plus d'historique. Si elle ne répond pas, la connexion reste faite : les opérations arriveront à la mise à jour
    // suivante, qui repart de la dernière opération gardée.
    public async Task ReadAsync(Guid userId, Guid connectionId, ProvidedSession session, UserPresence presence)
    {
        var today = UsersCalendar.FindToday(clock.GetUtcNow());
        var storedAccounts = await connectionRepository.FindAccountsAsync(new List<Guid> { connectionId });
        var transactionsByAccount = new Dictionary<Guid, List<ReceivedTransaction>>();

        foreach (var storedAccount in storedAccounts)
        {
            var sessionAccount = session.Accounts.FirstOrDefault(account => account.StableId == storedAccount.ExternalAccountId);

            if (sessionAccount == null)
            {
                continue;
            }

            var startDate = await transactionArrival.ChooseReadingStartAsync(storedAccount.Id, today);

            try
            {
                var providedTransactions = await bankProvider.ReadTransactionsAsync(sessionAccount.Uid, startDate, presence);
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

        await savingService.SaveWithRetriesAsync(
            () => PrepareFirstTransactionsAsync(userId, connectionId, transactionsByAccount),
            0);
    }

    // Un passage : vérifie que la connexion est toujours là, puis prépare les opérations nouvelles de chacun de ses comptes.
    private async Task<PreparedChanges<int>> PrepareFirstTransactionsAsync(
        Guid userId,
        Guid connectionId,
        Dictionary<Guid, List<ReceivedTransaction>> transactionsByAccount)
    {
        var connection = await connectionRepository.FindConnectionAsync(userId, connectionId);

        if (connection == null)
        {
            return PreparedChanges<int>.NothingToSave(0);
        }

        var accounts = await connectionRepository.FindAccountsAsync(new List<Guid> { connectionId });
        var preparedCount = 0;

        foreach (var account in accounts)
        {
            if (transactionsByAccount.TryGetValue(account.Id, out var receivedTransactions))
            {
                preparedCount = preparedCount + await transactionArrival.PrepareNewTransactionsAsync(userId, account.Id, receivedTransactions);
            }
        }

        if (preparedCount == 0)
        {
            return PreparedChanges<int>.NothingToSave(0);
        }

        return PreparedChanges<int>.SaveThenReturn(preparedCount);
    }
}
