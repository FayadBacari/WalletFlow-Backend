using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : les soldes sont mis à jour à chaque ouverture de l'application, et jusqu'à 4 fois par jour
// en arrière-plan (GR Comptes). Si la banque ne répond pas, le dernier solde reste affiché avec sa date.
public class BankBalanceUpdateService
{
    private readonly IBankDataProvider bankProvider;
    private readonly IBankConnectionRepository connectionRepository;
    private readonly IBankUpdateLimiter updateLimiter;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit le prestataire, l'accès aux connexions, la limite par utilisateur, l'enregistrement avec réessai et l'horloge.
    public BankBalanceUpdateService(
        IBankDataProvider bankProvider,
        IBankConnectionRepository connectionRepository,
        IBankUpdateLimiter updateLimiter,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.bankProvider = bankProvider;
        this.connectionRepository = connectionRepository;
        this.updateLimiter = updateLimiter;
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

    // Met à jour une connexion : lit sa session et ses soldes chez le prestataire, puis enregistre le résultat.
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
            ? new BankReading(BankReadingOutcome.SessionEnded, new Dictionary<Guid, int>())
            : await ReadBalancesAsync(connection, presence);

        if (reading.Outcome == BankReadingOutcome.TryLater)
        {
            return;
        }

        await savingService.SaveWithRetriesAsync(
            () => PrepareUpdateAsync(connectionId, readSessionId, reading, now),
            false);
    }

    // Lit chez le prestataire le solde de chaque compte gardé, retrouvé dans la session par son identifiant stable.
    private async Task<BankReading> ReadBalancesAsync(BankConnection connection, UserPresence? presence)
    {
        var storedAccounts = await connectionRepository.FindAccountsAsync(new List<Guid> { connection.Id });
        var balances = new Dictionary<Guid, int>();

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
            }

            return new BankReading(BankReadingOutcome.Read, balances);
        }
        catch (BankProviderException exception) when (exception.Failure == BankProviderFailure.SessionEnded)
        {
            return new BankReading(BankReadingOutcome.SessionEnded, balances);
        }
        catch (BankProviderException exception) when (exception.Failure == BankProviderFailure.RateLimited)
        {
            return new BankReading(BankReadingOutcome.TryLater, balances);
        }
        catch (BankProviderException)
        {
            return new BankReading(BankReadingOutcome.BankFailed, balances);
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
        }

        connection.RecordSuccessfulUpdate();
        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Ce que la lecture chez le prestataire a donné, et les soldes lus, par compte.
    private sealed record BankReading(BankReadingOutcome Outcome, Dictionary<Guid, int> Balances);

    // Les issues d'une lecture chez le prestataire.
    private enum BankReadingOutcome
    {
        Read,          // les soldes sont lus
        SessionEnded,  // l'autorisation est finie ou retirée : la connexion expire
        BankFailed,    // la banque ou le prestataire ne répond pas : la connexion passe en erreur
        TryLater       // la banque refuse pour l'instant : rien ne change, le prochain passage réessaiera
    }
}
