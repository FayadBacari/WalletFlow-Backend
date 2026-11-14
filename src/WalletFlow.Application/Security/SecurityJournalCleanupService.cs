namespace WalletFlow.Application.Security;

// Cas d'utilisation : efface les événements du journal de sécurité de plus d'un an (SS §11). Un journal gardé sans fin
// garderait aussi sans fin les adresses IP de chacun.
public class SecurityJournalCleanupService
{
    private readonly ISecurityEventRepository securityEventRepository;

    // Reçoit l'accès au journal.
    public SecurityJournalCleanupService(ISecurityEventRepository securityEventRepository)
    {
        this.securityEventRepository = securityEventRepository;
    }

    // Efface les événements concernés, et renvoie leur nombre.
    public async Task<int> DeleteExpiredEventsAsync()
    {
        return await securityEventRepository.DeleteExpiredEventsAsync();
    }
}
