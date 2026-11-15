using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Notifications;

// Cas d'utilisation : la tâche de fond prévient l'utilisateur 7 jours avant la fin de l'autorisation donnée à sa banque
// (GR Comptes), une seule fois par autorisation : un renouvellement efface l'alerte, et la nouvelle autorisation en
// déclenchera une autre. L'e-mail ne contient aucun lien, il invite seulement à ouvrir WalletFlow (SS §9).
public class BankAuthorizationAlertService
{
    private readonly IBankConnectionRepository connectionRepository;
    private readonly IAlertRepository alertRepository;
    private readonly AlertPreparation alertPreparation;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux connexions et aux alertes, la préparation des alertes, l'enregistrement avec réessai et l'horloge.
    public BankAuthorizationAlertService(
        IBankConnectionRepository connectionRepository,
        IAlertRepository alertRepository,
        AlertPreparation alertPreparation,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.connectionRepository = connectionRepository;
        this.alertRepository = alertRepository;
        this.alertPreparation = alertPreparation;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Donne les connexions dont l'utilisateur doit être prévenu maintenant.
    public async Task<List<Guid>> FindConnectionsToWarnAsync()
    {
        var now = clock.GetUtcNow().UtcDateTime;

        return await alertRepository.FindConnectionsToWarnAsync(now + BankConnectionRules.RenewalWarningDelay);
    }

    // Prévient l'utilisateur de cette connexion si son autorisation est toujours à renouveler. Renvoie vrai si l'alerte part.
    public async Task<bool> WarnAsync(Guid connectionId)
    {
        return await savingService.SaveWithRetriesAsync(
            () => PrepareWarningAsync(connectionId, clock.GetUtcNow().UtcDateTime),
            false);
    }

    // Un passage. La connexion est réécrite avec l'alerte, en vérifiant sa version : un renouvellement enregistré entre-temps
    // fait échouer cet enregistrement, et le passage suivant trouve la nouvelle date de fin. Dans l'autre sens, l'alerte
    // enregistrée d'abord fait échouer le renouvellement, qui relit tout et l'efface.
    private async Task<PreparedChanges<bool>> PrepareWarningAsync(Guid connectionId, DateTime now)
    {
        var connection = await connectionRepository.FindConnectionByIdAsync(connectionId);

        if (connection == null
            || !AlertRules.IsAuthorizationToRenew(connection.AuthorizationExpiresAt, now)
            || await alertRepository.FindConnectionAlertAsync(connectionId) != null)
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        if (!await alertPreparation.TryPrepareAsync(Alert.ForBankAuthorization(connection.UserId, connection.Id, now)))
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        connectionRepository.PrepareUnchangedCheck(connection);

        return PreparedChanges<bool>.SaveThenReturn(true);
    }
}
