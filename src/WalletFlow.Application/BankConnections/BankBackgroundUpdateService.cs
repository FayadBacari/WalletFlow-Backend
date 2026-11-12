namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : la tâche de fond met à jour les connexions jusqu'à 4 fois par jour, sans l'utilisateur (GR Comptes).
// Chaque connexion attend 6 heures entre deux mises à jour en arrière-plan : la banque n'en permet souvent pas plus.
public class BankBackgroundUpdateService
{
    private readonly IBankConnectionRepository connectionRepository;
    private readonly IBankUpdateSchedule updateSchedule;
    private readonly BankBalanceUpdateService updateService;

    // Reçoit l'accès aux connexions, l'horaire des mises à jour en arrière-plan, et la mise à jour d'une connexion.
    public BankBackgroundUpdateService(
        IBankConnectionRepository connectionRepository,
        IBankUpdateSchedule updateSchedule,
        BankBalanceUpdateService updateService)
    {
        this.connectionRepository = connectionRepository;
        this.updateSchedule = updateSchedule;
        this.updateService = updateService;
    }

    // Met à jour les connexions dont la dernière mise à jour en arrière-plan date de 6 heures ou plus, et renvoie leur nombre.
    public async Task<int> UpdateDueConnectionsAsync()
    {
        var connectionIds = await connectionRepository.FindConnectionIdsToUpdateAsync();
        var updatedCount = 0;

        foreach (var connectionId in connectionIds)
        {
            if (!updateSchedule.TryStartBackgroundUpdate(connectionId))
            {
                continue;
            }

            await updateService.UpdateConnectionAsync(connectionId, null);
            updatedCount = updatedCount + 1;
        }

        return updatedCount;
    }
}
