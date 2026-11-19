using System.Net;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountDeletion;

// Révoque chez le prestataire l'autorisation des connexions d'un compte en suppression, puis marque chacune expirée
// (GR Suppression, SS §7, DD §5.1). Ses comptes et ses opérations restent jusqu'à l'effacement : si l'utilisateur change d'avis,
// il n'a qu'à renouveler l'autorisation. Une révocation que le prestataire ne confirme pas reste à faire : la tâche de fond
// la réessaie à chaque passage, puis une dernière fois avant l'effacement, tant que la suppression reste demandée.
// Chaque révocation est notée au journal (SS §11).
public class DeletedAccountBankRevocation
{
    private readonly IBankConnectionRepository connectionRepository;
    private readonly IUserAccountRepository accountRepository;
    private readonly IBankDataProvider bankProvider;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;

    // Reçoit l'accès aux connexions et aux comptes, le prestataire, l'enregistrement et le journal.
    public DeletedAccountBankRevocation(
        IBankConnectionRepository connectionRepository,
        IUserAccountRepository accountRepository,
        IBankDataProvider bankProvider,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal)
    {
        this.connectionRepository = connectionRepository;
        this.accountRepository = accountRepository;
        this.bankProvider = bankProvider;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
    }

    // Donne les connexions pas encore révoquées des comptes en suppression : la tâche de fond les réessaie.
    public async Task<List<Guid>> FindConnectionsToRevokeAsync(CancellationToken cancellationToken = default)
    {
        return await connectionRepository.FindConnectionIdsToRevokeAsync(cancellationToken);
    }

    // Révoque chaque connexion de ce compte qui n'est pas encore expirée. Renvoie le nombre de révocations faites.
    public async Task<int> RevokeAccountConnectionsAsync(Guid userId, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        var connectionIds = (await connectionRepository.FindConnectionsAsync(userId, cancellationToken))
            .Select(connection => connection.Id)
            .ToList();
        var revokedCount = 0;

        foreach (var connectionId in connectionIds)
        {
            if (await TryRevokeAsync(connectionId, ipAddress, cancellationToken))
            {
                revokedCount = revokedCount + 1;
            }
        }

        return revokedCount;
    }

    // Révoque cette connexion chez le prestataire, puis la marque expirée. Renvoie faux si elle n'est plus à révoquer : déjà
    // expirée, ou son compte n'est plus en suppression, parce qu'une connexion vient de l'annuler et que l'utilisateur garde sa
    // banque. Renvoie faux aussi si le prestataire ne confirme pas : elle reste alors à révoquer.
    public async Task<bool> TryRevokeAsync(Guid connectionId, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        var connection = await connectionRepository.FindConnectionByIdAsync(connectionId, cancellationToken);
        if (connection == null || connection.Status == BankConnectionStatus.Expired)
        {
            return false;
        }

        var account = await accountRepository.FindByIdAsync(connection.UserId, cancellationToken);
        if (account?.DeletionRequestedAt == null)
        {
            return false;
        }

        var revokedExternalId = connection.ExternalConnectionId;

        try
        {
            await bankProvider.RevokeSessionAsync(revokedExternalId, cancellationToken);
        }
        catch (BankProviderException)
        {
            return false;
        }

        // La banque a révoqué l'autorisation : la marque ne s'annule plus, sinon la révocation serait refaite pour rien.
        return await savingService.SaveWithRetriesAsync(
            () => PrepareExpiryAsync(connectionId, revokedExternalId, ipAddress, CancellationToken.None),
            false,
            CancellationToken.None);
    }

    // Un passage : relit la connexion, et la marque expirée si sa session chez le prestataire est toujours celle qui vient d'être
    // révoquée. La connexion est contrôlée à l'enregistrement : une mise à jour ou un renouvellement simultané fait recommencer
    // le passage, et une nouvelle session reste à révoquer.
    private async Task<PreparedChanges<bool>> PrepareExpiryAsync(Guid connectionId, string revokedExternalId, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        var connection = await connectionRepository.FindConnectionByIdAsync(connectionId, cancellationToken);
        if (connection == null
            || connection.Status == BankConnectionStatus.Expired
            || connection.ExternalConnectionId != revokedExternalId)
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        connection.MarkExpired();
        connectionRepository.PrepareUnchangedCheck(connection);
        securityJournal.PrepareEvent(SecurityEventType.BankDisconnection, SecurityEventResult.Success, connection.UserId, ipAddress);

        return PreparedChanges<bool>.SaveThenReturn(true);
    }
}
