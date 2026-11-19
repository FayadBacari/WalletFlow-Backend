using System.Net;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : l'utilisateur déconnecte une banque (GR Comptes). L'autorisation est d'abord révoquée chez le prestataire,
// puis la connexion est supprimée avec ses comptes et leurs transactions. Si le prestataire ne confirme pas, rien n'est supprimé.
// Le site demande à l'utilisateur de confirmer avant d'appeler ce cas d'utilisation. Sans ces opérations, les dépenses du mois
// changent, et peuvent grandir si un remboursement part : les seuils de budget sont vérifiés juste après.
public class BankDisconnectionService
{
    // Un renouvellement peut changer la session entre sa révocation et la suppression, ou PostgreSQL arrêter la suppression
    // pour débloquer un interblocage : on recommence alors, avec la session lue à nouveau.
    public const int MaximumTries = 5;

    private readonly IBankDataProvider bankProvider;
    private readonly IBankConnectionRepository connectionRepository;
    private readonly BudgetAlertService budgetAlerts;
    private readonly SecurityJournalService securityJournal;

    // Reçoit le prestataire, l'accès aux connexions, la vérification des budgets et le journal de sécurité.
    public BankDisconnectionService(
        IBankDataProvider bankProvider,
        IBankConnectionRepository connectionRepository,
        BudgetAlertService budgetAlerts,
        SecurityJournalService securityJournal)
    {
        this.bankProvider = bankProvider;
        this.connectionRepository = connectionRepository;
        this.budgetAlerts = budgetAlerts;
        this.securityJournal = securityJournal;
    }

    // Révoque l'autorisation de la connexion, puis la supprime. La suppression ne se fait que si la session révoquée
    // est toujours celle de la connexion : aucune session renouvelée entre-temps ne reste ouverte chez le prestataire.
    public async Task<BankDisconnectionResult> DisconnectAsync(Guid userId, Guid connectionId, IPAddress? ipAddress, CancellationToken cancellationToken = default)
    {
        for (var tryNumber = 1; tryNumber <= MaximumTries; tryNumber++)
        {
            var externalConnectionId = await connectionRepository.ReadExternalIdAsync(userId, connectionId, cancellationToken);

            if (externalConnectionId == null)
            {
                return new BankDisconnectionResult.NotFound();
            }

            try
            {
                await bankProvider.RevokeSessionAsync(externalConnectionId, cancellationToken);
            }
            catch (BankProviderException)
            {
                return new BankDisconnectionResult.ProviderUnavailable();
            }

            // La banque a révoqué l'autorisation : la suppression ne s'annule plus, sinon WalletFlow garderait une connexion morte.
            if (await connectionRepository.DeleteAsync(userId, connectionId, externalConnectionId, CancellationToken.None))
            {
                await securityJournal.SaveEventNowAsync(SecurityEventType.BankDisconnection, SecurityEventResult.Success, userId, ipAddress);
                await budgetAlerts.CheckAsync(userId, cancellationToken);
                return new BankDisconnectionResult.Disconnected();
            }
        }

        return new BankDisconnectionResult.NotSaved();
    }
}
