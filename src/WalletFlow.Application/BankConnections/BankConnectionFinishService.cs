using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : l'utilisateur revient de sa banque, et WalletFlow termine la connexion ou son renouvellement (GR Comptes).
// Le state doit être celui que WalletFlow a donné à cet utilisateur (SS §7). Seuls les comptes en euros sont gardés.
public class BankConnectionFinishService
{
    private readonly IBankDataProvider bankProvider;
    private readonly IBankConnectionRepository connectionRepository;
    private readonly IBankAuthorizationStore authorizationStore;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly TimeProvider clock;

    // Reçoit le prestataire, l'accès aux connexions, la mémoire des autorisations commencées, le créateur de jetons,
    // l'enregistrement avec réessai, le journal de sécurité et l'horloge.
    public BankConnectionFinishService(
        IBankDataProvider bankProvider,
        IBankConnectionRepository connectionRepository,
        IBankAuthorizationStore authorizationStore,
        ISecureTokenGenerator tokenGenerator,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        TimeProvider clock)
    {
        this.bankProvider = bankProvider;
        this.connectionRepository = connectionRepository;
        this.authorizationStore = authorizationStore;
        this.tokenGenerator = tokenGenerator;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.clock = clock;
    }

    // Termine l'autorisation avec le code et le state renvoyés par la banque.
    public async Task<BankConnectionFinishResult> FinishAsync(Guid userId, string? code, string? state, UserPresence presence)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return new BankConnectionFinishResult.LinkNoLongerValid();
        }

        var authorization = authorizationStore.TryTake(tokenGenerator.HashToken(state), userId);

        // SS §7 : un state inconnu, expiré, déjà utilisé ou donné à un autre compte peut venir d'un lien piégé.
        if (authorization == null)
        {
            await securityJournal.SaveEventNowAsync(SecurityEventType.BankConnection, SecurityEventResult.Failure, userId, presence.IpAddress);
            return new BankConnectionFinishResult.LinkNoLongerValid();
        }

        ProvidedSession session;
        try
        {
            session = await bankProvider.CreateSessionAsync(code);
        }
        catch (BankProviderException exception) when (exception.Failure == BankProviderFailure.AuthorizationRefused)
        {
            await securityJournal.SaveEventNowAsync(SecurityEventType.BankConnection, SecurityEventResult.Failure, userId, presence.IpAddress);
            return new BankConnectionFinishResult.RefusedByBank();
        }
        catch (BankProviderException)
        {
            return new BankConnectionFinishResult.ProviderUnavailable();
        }

        var result = await KeepSessionAsync(userId, authorization, session, presence);

        // Une session qui n'a rien donné est révoquée tout de suite : WalletFlow ne garde aucun accès inutile.
        if (result is not BankConnectionFinishResult.Connected && result is not BankConnectionFinishResult.Renewed)
        {
            await RevokeQuietlyAsync(session.SessionId);
        }

        return result;
    }

    // Lit les comptes de la nouvelle session, puis enregistre la connexion ou son renouvellement.
    private async Task<BankConnectionFinishResult> KeepSessionAsync(
        Guid userId,
        PendingBankAuthorization authorization,
        ProvidedSession session,
        UserPresence presence)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var authorizationEnd = CapAuthorizationEnd(session.ValidUntil, now);

        if (string.IsNullOrWhiteSpace(session.SessionId)
            || session.SessionId.Length > BankConnectionRules.MaximumExternalIdLength
            || authorizationEnd <= now)
        {
            return new BankConnectionFinishResult.ProviderUnavailable();
        }

        var skippedAccountNames = new List<string>();
        var receivedAccounts = await ReadAccountsAsync(session, presence, skippedAccountNames);

        if (receivedAccounts == null)
        {
            return new BankConnectionFinishResult.ProviderUnavailable();
        }

        string? replacedSessionId = null;
        var result = await savingService.SaveWithRetriesAsync(
            async () =>
            {
                if (authorization.RenewedConnectionId is not Guid renewedConnectionId)
                {
                    return await PrepareNewConnectionAsync(userId, authorization.BankName, session.SessionId, authorizationEnd, receivedAccounts, skippedAccountNames, presence, now);
                }

                var renewal = await PrepareRenewalAsync(userId, renewedConnectionId, session.SessionId, authorizationEnd, receivedAccounts, skippedAccountNames, presence, now);
                replacedSessionId = renewal.ReplacedSessionId;
                return renewal.Changes;
            },
            new BankConnectionFinishResult.NotSaved());

        // L'ancienne session d'une connexion renouvelée ne sert plus : elle est révoquée.
        if (result is BankConnectionFinishResult.Renewed && replacedSessionId != null && replacedSessionId != session.SessionId)
        {
            await RevokeQuietlyAsync(replacedSessionId);
        }

        return result;
    }

    // Un passage : ajoute la connexion et ses comptes, sauf ceux déjà présents dans une autre connexion de l'utilisateur.
    private async Task<PreparedChanges<BankConnectionFinishResult>> PrepareNewConnectionAsync(
        Guid userId,
        string bankName,
        string sessionId,
        DateTime authorizationEnd,
        List<ReceivedAccount> receivedAccounts,
        List<string> skippedAccountNames,
        UserPresence presence,
        DateTime now)
    {
        var skippedNames = new List<string>(skippedAccountNames);
        var accountsToAdd = new List<ReceivedAccount>();

        foreach (var account in receivedAccounts)
        {
            if (await connectionRepository.IsAccountConnectedElsewhereAsync(userId, account.StableId, null))
            {
                skippedNames.Add(account.Name);
                continue;
            }

            accountsToAdd.Add(account);
        }

        if (accountsToAdd.Count == 0)
        {
            return PreparedChanges<BankConnectionFinishResult>.NothingToSave(
                new BankConnectionFinishResult.NoAccountAdded { SkippedAccountNames = skippedNames });
        }

        var connection = new BankConnection
        {
            ExternalConnectionId = sessionId,
            BankName = BankConnectionRules.CleanReceivedName(bankName, BankConnectionRules.MaximumBankNameLength, BankConnectionRules.BankNameReplacement),
            AuthorizationExpiresAt = authorizationEnd,
            UserId = userId
        };
        connectionRepository.PrepareConnection(connection);

        foreach (var account in accountsToAdd)
        {
            connectionRepository.PrepareAccount(CreateAccount(account, connection.Id, now));
        }

        securityJournal.PrepareEvent(SecurityEventType.BankConnection, SecurityEventResult.Success, userId, presence.IpAddress);

        return PreparedChanges<BankConnectionFinishResult>.SaveThenReturn(new BankConnectionFinishResult.Connected
        {
            ConnectionId = connection.Id,
            AddedAccountCount = accountsToAdd.Count,
            SkippedAccountNames = skippedNames
        });
    }

    // Un passage : renouvelle la connexion, met à jour ses comptes, et ajoute ceux qui sont nouveaux.
    // Les comptes que la banque ne donne plus restent, avec leurs transactions.
    private async Task<RenewalChanges> PrepareRenewalAsync(
        Guid userId,
        Guid connectionId,
        string sessionId,
        DateTime authorizationEnd,
        List<ReceivedAccount> receivedAccounts,
        List<string> skippedAccountNames,
        UserPresence presence,
        DateTime now)
    {
        var connection = await connectionRepository.FindConnectionAsync(userId, connectionId);

        if (connection == null)
        {
            return new RenewalChanges(
                PreparedChanges<BankConnectionFinishResult>.NothingToSave(new BankConnectionFinishResult.ConnectionGone()),
                null);
        }

        var replacedSessionId = connection.ExternalConnectionId;
        var storedAccounts = await connectionRepository.FindAccountsAsync(new List<Guid> { connection.Id });
        var skippedNames = new List<string>(skippedAccountNames);
        var addedCount = 0;

        foreach (var account in receivedAccounts)
        {
            var storedAccount = storedAccounts.FirstOrDefault(stored => stored.ExternalAccountId == account.StableId);

            if (storedAccount != null)
            {
                storedAccount.Rename(account.Name);
                storedAccount.UpdateBalance(account.BalanceInCents, now);
                continue;
            }

            if (await connectionRepository.IsAccountConnectedElsewhereAsync(userId, account.StableId, connection.Id))
            {
                skippedNames.Add(account.Name);
                continue;
            }

            connectionRepository.PrepareAccount(CreateAccount(account, connection.Id, now));
            addedCount = addedCount + 1;
        }

        connection.Renew(sessionId, authorizationEnd);
        securityJournal.PrepareEvent(SecurityEventType.BankConnection, SecurityEventResult.Success, userId, presence.IpAddress);

        var changes = PreparedChanges<BankConnectionFinishResult>.SaveThenReturn(new BankConnectionFinishResult.Renewed
        {
            ConnectionId = connection.Id,
            AddedAccountCount = addedCount,
            SkippedAccountNames = skippedNames
        });

        return new RenewalChanges(changes, replacedSessionId);
    }

    // Lit le solde de chaque compte en euros, l'utilisateur étant là. Les autres comptes sont nommés dans la liste des comptes laissés.
    // Renvoie null si le prestataire ou la banque ne répond pas.
    private async Task<List<ReceivedAccount>?> ReadAccountsAsync(ProvidedSession session, UserPresence presence, List<string> skippedAccountNames)
    {
        var receivedAccounts = new List<ReceivedAccount>();

        try
        {
            foreach (var account in session.Accounts)
            {
                var name = BankConnectionRules.CleanReceivedName(account.Name, BankConnectionRules.MaximumAccountNameLength, BankConnectionRules.AccountNameReplacement);

                if (account.Currency != BankConnectionRules.AccountCurrency
                    || string.IsNullOrEmpty(account.StableId)
                    || account.StableId.Length > BankConnectionRules.MaximumExternalIdLength)
                {
                    skippedAccountNames.Add(name);
                    continue;
                }

                var balance = ProvidedBalanceChoice.Choose(await bankProvider.ReadBalancesAsync(account.Uid, presence));

                if (balance == null)
                {
                    skippedAccountNames.Add(name);
                    continue;
                }

                receivedAccounts.Add(new ReceivedAccount(account.StableId, name, balance.Value));
            }
        }
        catch (BankProviderException)
        {
            return null;
        }

        return receivedAccounts;
    }

    // Garde la fin donnée par le prestataire, sans jamais dépasser 180 jours (DD §5.1).
    private static DateTime CapAuthorizationEnd(DateTime providedEnd, DateTime now)
    {
        var latestEnd = now + BankConnectionRules.MaximumAuthorizationDuration;

        if (providedEnd > latestEnd)
        {
            return latestEnd;
        }

        return providedEnd;
    }

    // Crée un compte de la connexion, avec le solde que la banque vient de donner.
    private static BankAccount CreateAccount(ReceivedAccount account, Guid connectionId, DateTime now)
    {
        return new BankAccount
        {
            ExternalAccountId = account.StableId,
            Name = account.Name,
            BalanceInCents = account.BalanceInCents,
            BalanceUpdatedAt = now,
            BankConnectionId = connectionId
        };
    }

    // Révoque une session sans faire échouer la requête : si le prestataire ne répond pas, elle finira seule à sa date.
    private async Task RevokeQuietlyAsync(string sessionId)
    {
        try
        {
            await bankProvider.RevokeSessionAsync(sessionId);
        }
        catch (BankProviderException)
        {
        }
    }

    // Un compte reçu de la banque, prêt à être gardé : son identifiant stable, son nom enregistré et son solde.
    private sealed record ReceivedAccount(string StableId, string Name, int BalanceInCents);

    // Ce qu'un passage de renouvellement a préparé, et l'ancienne session de la connexion, à révoquer après l'enregistrement.
    private sealed record RenewalChanges(PreparedChanges<BankConnectionFinishResult> Changes, string? ReplacedSessionId);
}
