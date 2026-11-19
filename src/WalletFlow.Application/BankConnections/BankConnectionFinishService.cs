using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : l'utilisateur revient de sa banque, et WalletFlow termine la connexion ou son renouvellement (GR Comptes).
// Le state doit être celui que WalletFlow a donné à cet utilisateur (SS §7). Seuls les comptes en euros sont gardés.
// Les opérations des comptes gardés sont lues aussitôt, l'utilisateur étant là. Un renouvellement efface l'alerte de l'ancienne
// autorisation : la nouvelle préviendra à son tour 7 jours avant sa fin.
public class BankConnectionFinishService
{
    private readonly IBankDataProvider bankProvider;
    private readonly IBankConnectionRepository connectionRepository;
    private readonly IAlertRepository alertRepository;
    private readonly IBankAuthorizationStore authorizationStore;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly BankFirstTransactionsReading firstTransactionsReading;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly TimeProvider clock;

    // Reçoit le prestataire, l'accès aux connexions et aux alertes, la mémoire des autorisations commencées, le créateur de jetons,
    // la première lecture des opérations, l'enregistrement avec réessai, le journal de sécurité et l'horloge.
    public BankConnectionFinishService(
        IBankDataProvider bankProvider,
        IBankConnectionRepository connectionRepository,
        IAlertRepository alertRepository,
        IBankAuthorizationStore authorizationStore,
        ISecureTokenGenerator tokenGenerator,
        BankFirstTransactionsReading firstTransactionsReading,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        TimeProvider clock)
    {
        this.bankProvider = bankProvider;
        this.connectionRepository = connectionRepository;
        this.alertRepository = alertRepository;
        this.authorizationStore = authorizationStore;
        this.tokenGenerator = tokenGenerator;
        this.firstTransactionsReading = firstTransactionsReading;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.clock = clock;
    }

    // Termine l'autorisation avec le code et le state renvoyés par la banque.
    public async Task<BankConnectionFinishResult> FinishAsync(Guid userId, string? code, string? state, UserPresence presence, CancellationToken cancellationToken = default)
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
            session = await bankProvider.CreateSessionAsync(code, cancellationToken);
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

        // La session existe déjà chez le prestataire : la suite ne s'annule plus, sinon elle y resterait sans connexion dans WalletFlow.
        var result = await KeepSessionAsync(userId, authorization, session, presence, CancellationToken.None);

        if (result is BankConnectionFinishResult.Connected connected)
        {
            await firstTransactionsReading.ReadAsync(userId, connected.ConnectionId, session, presence, cancellationToken);
            return result;
        }

        if (result is BankConnectionFinishResult.Renewed renewed)
        {
            await firstTransactionsReading.ReadAsync(userId, renewed.ConnectionId, session, presence, cancellationToken);
            return result;
        }

        // Une session qui n'a rien donné est révoquée tout de suite : WalletFlow ne garde aucun accès inutile.
        await RevokeQuietlyAsync(session.SessionId, cancellationToken);
        return result;
    }

    // Lit les comptes de la nouvelle session, puis enregistre la connexion ou son renouvellement.
    private async Task<BankConnectionFinishResult> KeepSessionAsync(
        Guid userId,
        PendingBankAuthorization authorization,
        ProvidedSession session,
        UserPresence presence,
        CancellationToken cancellationToken = default)
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
        var receivedAccounts = await ReadAccountsAsync(session, presence, skippedAccountNames, cancellationToken);

        if (receivedAccounts == null)
        {
            return new BankConnectionFinishResult.ProviderUnavailable();
        }

        var keptSession = new KeptSession(userId, session.SessionId, authorizationEnd, receivedAccounts, skippedAccountNames, presence, now);
        string? replacedSessionId = null;
        var result = await savingService.SaveWithRetriesAsync(
            async () =>
            {
                if (authorization.RenewedConnectionId is not Guid renewedConnectionId)
                {
                    return await PrepareNewConnectionAsync(keptSession, authorization.BankName, cancellationToken);
                }

                var renewal = await PrepareRenewalAsync(keptSession, renewedConnectionId, cancellationToken);
                replacedSessionId = renewal.ReplacedSessionId;
                return renewal.Changes;
            },
            new BankConnectionFinishResult.NotSaved(),
            cancellationToken);

        // L'ancienne session d'une connexion renouvelée ne sert plus : elle est révoquée.
        if (result is BankConnectionFinishResult.Renewed && replacedSessionId != null && replacedSessionId != session.SessionId)
        {
            await RevokeQuietlyAsync(replacedSessionId, cancellationToken);
        }

        return result;
    }

    // Un passage : ajoute la connexion et ses comptes, sauf ceux déjà présents dans une autre connexion de l'utilisateur.
    private async Task<PreparedChanges<BankConnectionFinishResult>> PrepareNewConnectionAsync(KeptSession kept, string bankName, CancellationToken cancellationToken = default)
    {
        var skippedNames = new List<string>(kept.SkippedAccountNames);
        var accountsToAdd = new List<ReceivedAccount>();

        foreach (var account in kept.Accounts)
        {
            if (await connectionRepository.IsAccountConnectedElsewhereAsync(kept.UserId, account.StableId, null, cancellationToken))
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
            ExternalConnectionId = kept.SessionId,
            BankName = BankConnectionRules.CleanReceivedName(bankName, BankConnectionRules.MaximumBankNameLength, BankConnectionRules.BankNameReplacement),
            AuthorizationExpiresAt = kept.AuthorizationEnd,
            UserId = kept.UserId
        };
        connectionRepository.PrepareConnection(connection);

        foreach (var account in accountsToAdd)
        {
            connectionRepository.PrepareAccount(CreateAccount(account, connection.Id, kept.Now));
        }

        securityJournal.PrepareEvent(SecurityEventType.BankConnection, SecurityEventResult.Success, kept.UserId, kept.Presence.IpAddress);

        return PreparedChanges<BankConnectionFinishResult>.SaveThenReturn(new BankConnectionFinishResult.Connected
        {
            ConnectionId = connection.Id,
            AddedAccountCount = accountsToAdd.Count,
            SkippedAccountNames = skippedNames
        });
    }

    // Un passage : renouvelle la connexion, met à jour ses comptes, ajoute ceux qui sont nouveaux, et efface l'alerte de
    // l'ancienne autorisation. Les comptes que la banque ne donne plus restent, avec leurs transactions.
    private async Task<RenewalChanges> PrepareRenewalAsync(KeptSession kept, Guid connectionId, CancellationToken cancellationToken = default)
    {
        var connection = await connectionRepository.FindConnectionAsync(kept.UserId, connectionId, cancellationToken);

        if (connection == null)
        {
            return new RenewalChanges(
                PreparedChanges<BankConnectionFinishResult>.NothingToSave(new BankConnectionFinishResult.ConnectionGone()),
                null);
        }

        var replacedSessionId = connection.ExternalConnectionId;
        var storedAccounts = await connectionRepository.FindAccountsAsync(new List<Guid> { connection.Id }, cancellationToken);
        var skippedNames = new List<string>(kept.SkippedAccountNames);
        var addedCount = 0;

        foreach (var account in kept.Accounts)
        {
            var storedAccount = storedAccounts.FirstOrDefault(stored => stored.ExternalAccountId == account.StableId);

            if (storedAccount != null)
            {
                storedAccount.Rename(account.Name);
                storedAccount.UpdateBalance(account.BalanceInCents, kept.Now);
                continue;
            }

            if (await connectionRepository.IsAccountConnectedElsewhereAsync(kept.UserId, account.StableId, connection.Id, cancellationToken))
            {
                skippedNames.Add(account.Name);
                continue;
            }

            connectionRepository.PrepareAccount(CreateAccount(account, connection.Id, kept.Now));
            addedCount = addedCount + 1;
        }

        var alert = await alertRepository.FindConnectionAlertAsync(connection.Id, cancellationToken);

        if (alert != null)
        {
            alertRepository.PrepareDeletion(alert);
        }

        connection.Renew(kept.SessionId, kept.AuthorizationEnd);
        securityJournal.PrepareEvent(SecurityEventType.BankConnection, SecurityEventResult.Success, kept.UserId, kept.Presence.IpAddress);

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
    private async Task<List<ReceivedAccount>?> ReadAccountsAsync(ProvidedSession session, UserPresence presence, List<string> skippedAccountNames, CancellationToken cancellationToken = default)
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

                var balance = ProvidedBalanceChoice.Choose(await bankProvider.ReadBalancesAsync(account.Uid, presence, cancellationToken));

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
    private async Task RevokeQuietlyAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await bankProvider.RevokeSessionAsync(sessionId, cancellationToken);
        }
        catch (BankProviderException)
        {
        }
    }

    // Une session que la banque vient de donner, prête à être gardée : l'utilisateur, l'identifiant de la session chez
    // le prestataire, la fin de l'autorisation, les comptes reçus et ceux laissés de côté, la présence de l'utilisateur et l'heure.
    private sealed record KeptSession(
        Guid UserId,
        string SessionId,
        DateTime AuthorizationEnd,
        List<ReceivedAccount> Accounts,
        List<string> SkippedAccountNames,
        UserPresence Presence,
        DateTime Now);

    // Un compte reçu de la banque, prêt à être gardé : son identifiant stable, son nom enregistré et son solde.
    private sealed record ReceivedAccount(string StableId, string Name, int BalanceInCents);

    // Ce qu'un passage de renouvellement a préparé, et l'ancienne session de la connexion, à révoquer après l'enregistrement.
    private sealed record RenewalChanges(PreparedChanges<BankConnectionFinishResult> Changes, string? ReplacedSessionId);
}
