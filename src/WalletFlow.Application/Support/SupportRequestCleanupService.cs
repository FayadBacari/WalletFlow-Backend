using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Support;

// Cas d'utilisation : efface les demandes de support un an après leur envoi, avec leurs réponses (GR Admin, SS §7).
public class SupportRequestCleanupService
{
    private readonly ISupportRequestRepository supportRepository;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux demandes et l'horloge.
    public SupportRequestCleanupService(ISupportRequestRepository supportRepository, TimeProvider clock)
    {
        this.supportRepository = supportRepository;
        this.clock = clock;
    }

    // Efface les demandes envoyées il y a plus d'un an, et renvoie leur nombre. Une demande envoyée il y a un an
    // exactement est encore gardée.
    public async Task<int> DeleteOldRequestsAsync()
    {
        var oldestKeptSendingTime = SupportRules.FindOldestKeptSendingTime(clock.GetUtcNow().UtcDateTime);

        return await supportRepository.DeleteSentBeforeAsync(oldestKeptSendingTime);
    }
}
