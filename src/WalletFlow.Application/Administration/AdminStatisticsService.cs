namespace WalletFlow.Application.Administration;

// Cas d'utilisation : l'administrateur voit des statistiques globales et anonymes (GR Admin) : les comptes par état, les
// connexions bancaires actives et les demandes de support par état. Aucun chiffre ne porte sur l'argent.
public class AdminStatisticsService
{
    private readonly IAdministrationRepository administrationRepository;
    private readonly TimeProvider clock;

    // Reçoit l'accès de l'administrateur et l'horloge qui dit quels comptes sont bloqués.
    public AdminStatisticsService(IAdministrationRepository administrationRepository, TimeProvider clock)
    {
        this.administrationRepository = administrationRepository;
        this.clock = clock;
    }

    // Compte tout, à ce moment.
    public async Task<AdministrationStatistics> ReadAsync(CancellationToken cancellationToken = default)
    {
        return await administrationRepository.CountStatisticsAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}
