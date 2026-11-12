using WalletFlow.Application.Persistence;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : les soldes et les opérations sont mis à jour à chaque ouverture de l'application, et jusqu'à 4 fois
// par jour en arrière-plan (GR Comptes). Si la banque ne répond pas, le dernier solde reste affiché avec sa date.
public class BankBalanceUpdateService
{
    private readonly IBankDataProvider bankProvider;
    private readonly IBankConnectionRepository connectionRepository;
    private readonly IBankUpdateLimiter updateLimiter;
    private readonly BankTransactionArrival transactionArrival;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit le prestataire, l'accès aux connexions, la limite par utilisateur, l'arrivée des opérations,
    // l'enregistrement avec réessai et l'horloge.
    public BankBalanceUpdateService(
        IBankDataProvider bankProvider,
        IBankConnectionRepository connectionRepository,
        IBankUpdateLimiter updateLimiter,
        BankTransactionArrival transactionArrival,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.bankProvider = bankProvider;
        this.connectionRepository = connectionRepository;
        this.updateLimiter = updateLimiter;
        this.transactionArrival = transactionArrival;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Met à jour toutes les connexions de l'utilisateur, qui est là : une fois par minute au plus (SS §6).
    public async Task<BankUpdateResult> UpdateForUserAsync(Guid userId, UserPresence presence)
    {
        if (!updateLimiter.TryCountUpdate(userId))
        {
            return new BankUpdateResult.TooManyUpdates();
        }

        var connections = await connectionRepository.FindConnectionsAsync(userId);

        foreach (var connection in connections)
        {
            await UpdateConnectionAsync(connection.Id, presence);
        }

        return new BankUpdateResult.Updated();
    }

    // Met à jour une connexion : lit sa session, ses soldes et ses opérations chez le prestataire, puis enregistre le résultat.
    // Sans l'utilisateur (presence null), la lecture compte dans les 4 lectures par jour de la banque.
    public async Task UpdateConnectionAsync(Guid connectionId, UserPresence? presence)
    {
        var connection = await connectionRepository.FindConnectionByIdAsync(connectionId);

        if (connection == null || connection.Status == BankConnectionStatus.Expired)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var readSessionId = connection.ExternalConnectionId;
        var reading = connection.HasAuthorizationEnded(now)
            ? new BankReading(BankReadingOutcome.SessionEnded, new Dictionary<Guid, int>(), new Dictionary<Guid, List<ReceivedTransaction>>())
            : await ReadBankAsync(connection, presence, DateOnly.FromDateTime(now));

        if (reading.Outcome == BankReadingOutcome.TryLater)
        {
            return;
        }

        await savingService.SaveWithRetriesAsync(
            () => PrepareUpdateAsync(connectionId, readSessionId, reading, now),
            false);
    }

    // Lit chez le prestataire le solde et les opérations de chaque compte gardé, retrouvé dans la session par son identifiant stable.
    private async Task<BankReading> ReadBankAsync(BankConnection connection, UserPresence? presence, DateOnly today)
    {
        var storedAccounts = await connectionRepository.FindAccountsAsync(new List<Guid> { connection.Id });
        var balances = new Dictionary<Guid, int>();
        var transactions = new Dictionary<Guid, List<ReceivedTransaction>>();

        try
        {
            var sessionAccounts = await bankProvider.ReadSessionAccountsAsync(connection.ExternalConnectionId);

            foreach (var storedAccount in storedAccounts)
            {
                var sessionAccount = sessionAccounts.FirstOrDefault(account => account.StableId == storedAccount.ExternalAccountId);

                if (sessionAccount == null)
                {
                    continue;
                }

                var balance = ProvidedBalanceChoice.Choose(await bankProvider.ReadBalancesAsync(sessionAccount.Uid, presence));

                if (balance != null)
                {
                    balances[storedAccount.Id] = balance.Value;
                }

                var receivedTransactions = await ReadTransactionsAsync(storedAccount.Id, sessionAccount.Uid, presence, today);

                if (receivedTransactions != null)
                {
                    transactions[storedAccount.Id] = receivedTransactions;
                }
            }

            return new BankReading(BankReadingOutcome.Read, balances, transactions);
        }
        catch (BankProviderException exception) when (exception.Failure == BankProviderFailure.SessionEnded)
        {
            return new BankReading(BankReadingOutcome.SessionEnded, balances, transactions);
        }
        catch (BankProviderException exception) when (exception.Failure == BankProviderFailure.RateLimited)
        {
            return new BankReading(BankReadingOutcome.TryLater, balances, transactions);
        }
        catch (BankProviderException)
        {
            return new BankReading(BankReadingOutcome.BankFailed, balances, transactions);
        }
    }

    // Lit les opérations du compte depuis le jour choisi. Si la banque refuse ou ne répond pas pour les opérations,
    // le solde déjà lu est quand même enregistré : les opérations arriveront à la mise à jour suivante, qui repart
    // de la dernière opération gardée. Une session finie, elle, fait expirer toute la connexion.
    private async Task<List<ReceivedTransaction>?> ReadTransactionsAsync(Guid accountId, string accountUid, UserPresence? presence, DateOnly today)
    {
        var startDate = await transactionArrival.ChooseReadingStartAsync(accountId, today);

        try
        {
            return TransactionReception.Receive(await bankProvider.ReadTransactionsAsync(accountUid, startDate, presence));
        }
        catch (BankProviderException exception) when (exception.Failure != BankProviderFailure.SessionEnded)
        {
            return null;
        }
    }

    // Un passage : relit la connexion, puis note ce que la lecture a donné. Une connexion supprimée ou renouvelée entre-temps
    // n'est pas touchée : ce qui a été lu appartenait à son ancienne session.
    private async Task<PreparedChanges<bool>> PrepareUpdateAsync(Guid connectionId, string readSessionId, BankReading reading, DateTime now)
    {
        var connection = await connectionRepository.FindConnectionByIdAsync(connectionId);

        if (connection == null
            || connection.ExternalConnectionId != readSessionId
            || connection.Status == BankConnectionStatus.Expired)
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        if (reading.Outcome == BankReadingOutcome.SessionEnded)
        {
            connection.MarkExpired();
            return PreparedChanges<bool>.SaveThenReturn(true);
        }

        if (reading.Outcome == BankReadingOutcome.BankFailed)
        {
            connection.RecordBankFailure();
            return PreparedChanges<bool>.SaveThenReturn(true);
        }

        var accounts = await connectionRepository.FindAccountsAsync(new List<Guid> { connectionId });

        foreach (var account in accounts)
        {
            if (reading.Balances.TryGetValue(account.Id, out var balanceInCents))
            {
                account.UpdateBalance(balanceInCents, now);
            }

            if (reading.Transactions.TryGetValue(account.Id, out var receivedTransactions))
            {
                await transactionArrival.PrepareNewTransactionsAsync(connection.UserId, account.Id, receivedTransactions);
            }
        }

        connection.RecordSuccessfulUpdate();
        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Ce que la lecture chez le prestataire a donné : les soldes lus et les opérations reçues, par compte.
    private sealed record BankReading(
        BankReadingOutcome Outcome,
        Dictionary<Guid, int> Balances,
        Dictionary<Guid, List<ReceivedTransaction>> Transactions);

    // Les issues d'une lecture chez le prestataire.
    private enum BankReadingOutcome
    {
        Read,          // les soldes et les opérations sont lus
        SessionEnded,  // l'autorisation est finie ou retirée : la connexion expire
        BankFailed,    // la banque ou le prestataire ne répond pas : la connexion passe en erreur
        TryLater       // la banque refuse pour l'instant : rien ne change, le prochain passage réessaiera
    }
}
