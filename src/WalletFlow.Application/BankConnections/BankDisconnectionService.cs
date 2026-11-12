using System.Net;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : l'utilisateur déconnecte une banque (GR Comptes). L'autorisation est d'abord révoquée chez le prestataire,
// puis la connexion est supprimée avec ses comptes et leurs transactions. Si le prestataire ne confirme pas, rien n'est supprimé.
// Le site demande à l'utilisateur de confirmer avant d'appeler ce cas d'utilisation.
public class BankDisconnectionService
{
    // Un renouvellement peut changer la session entre sa révocation et la suppression, ou PostgreSQL arrêter la suppression
    // pour débloquer un interblocage : on recommence alors, avec la session lue à nouveau.
    public const int MaximumTries = 5;

    private readonly IBankDataProvider bankProvider;
    private readonly IBankConnectionRepository connectionRepository;
    private readonly SecurityJournalService securityJournal;

    // Reçoit le prestataire, l'accès aux connexions et le journal de sécurité.
    public BankDisconnectionService(
        IBankDataProvider bankProvider,
        IBankConnectionRepository connectionRepository,
        SecurityJournalService securityJournal)
    {
        this.bankProvider = bankProvider;
        this.connectionRepository = connectionRepository;
        this.securityJournal = securityJournal;
    }

    // Révoque l'autorisation de la connexion, puis la supprime. La suppression ne se fait que si la session révoquée
    // est toujours celle de la connexion : aucune session renouvelée entre-temps ne reste ouverte chez le prestataire.
    public async Task<BankDisconnectionResult> DisconnectAsync(Guid userId, Guid connectionId, IPAddress? ipAddress)
    {
        for (var tryNumber = 1; tryNumber <= MaximumTries; tryNumber++)
        {
            var externalConnectionId = await connectionRepository.ReadExternalIdAsync(userId, connectionId);

            if (externalConnectionId == null)
            {
                return new BankDisconnectionResult.NotFound();
            }

            try
            {
                await bankProvider.RevokeSessionAsync(externalConnectionId);
            }
            catch (BankProviderException)
            {
                return new BankDisconnectionResult.ProviderUnavailable();
            }

            if (await connectionRepository.DeleteAsync(userId, connectionId, externalConnectionId))
            {
                await securityJournal.SaveEventNowAsync(SecurityEventType.BankDisconnection, SecurityEventResult.Success, userId, ipAddress);
                return new BankDisconnectionResult.Disconnected();
            }
        }

        return new BankDisconnectionResult.NotSaved();
    }
}
