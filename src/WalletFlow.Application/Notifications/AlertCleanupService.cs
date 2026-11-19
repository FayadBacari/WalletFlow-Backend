using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Notifications;

// Cas d'utilisation : efface les alertes de budget et de suggestion deux mois après leur mois (SS §7). Elles ne servaient
// qu'à ne jamais envoyer deux fois la même : passé ce délai, leur mois ne peut plus en déclencher.
public class AlertCleanupService
{
    private readonly IAlertRepository alertRepository;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux alertes et l'horloge.
    public AlertCleanupService(IAlertRepository alertRepository, TimeProvider clock)
    {
        this.alertRepository = alertRepository;
        this.clock = clock;
    }

    // Efface les alertes concernées, et renvoie leur nombre. Le mois en cours est celui de Paris.
    public async Task<int> DeleteOldAlertsAsync(CancellationToken cancellationToken = default)
    {
        var firstKeptMonth = AlertRules.FindFirstKeptMonth(UsersCalendar.FindToday(clock.GetUtcNow()));

        return await alertRepository.DeleteMonthAlertsBeforeAsync(firstKeptMonth, cancellationToken);
    }
}
