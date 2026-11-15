using WalletFlow.Application.Emails;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Notifications;

// Prépare une alerte et son e-mail dans le passage en cours de l'appelant : les deux sont enregistrés ensemble, ou rien
// (DD §9.3). Une alerte que l'utilisateur a désactivée n'est pas créée.
public class AlertPreparation
{
    private readonly IAlertRepository alertRepository;
    private readonly IEmailRequestRepository emailRequests;

    // Reçoit l'accès aux alertes et la file des e-mails.
    public AlertPreparation(IAlertRepository alertRepository, IEmailRequestRepository emailRequests)
    {
        this.alertRepository = alertRepository;
        this.emailRequests = emailRequests;
    }

    // Prépare l'alerte et son e-mail, sauf si l'utilisateur a désactivé ce type. Renvoie vrai si l'alerte est préparée.
    public async Task<bool> TryPrepareAsync(Alert alert)
    {
        if (!await alertRepository.IsEnabledAsync(alert.UserId, alert.Kind))
        {
            return false;
        }

        PrepareWithEmail(alert);
        return true;
    }

    // Prépare l'alerte et son e-mail. L'appelant a déjà vérifié que le type est activé.
    public void PrepareWithEmail(Alert alert)
    {
        alertRepository.PrepareAlert(alert);
        emailRequests.PrepareRequest(EmailRequest.ForAlert(alert));
    }

    // Prépare l'alerte seule : un seuil de budget atteint en même temps qu'un seuil plus haut est noté comme passé,
    // sans e-mail, pour ne pas partir plus tard dans le mois.
    public void PrepareWithoutEmail(Alert alert)
    {
        alertRepository.PrepareAlert(alert);
    }
}
