namespace WalletFlow.Domain.NotificationsAndSupport;

// Réponse de l'administrateur à une demande de support.
// Correspond à la table reponse_support du MPD (DD §10.2).
public class SupportResponse
{
    // id_reponse
    public Guid Id { get; set; } = Guid.NewGuid();

    // contenu_reponse
    public string Content { get; set; } = string.Empty;

    // id_demande
    public Guid SupportRequestId { get; set; }
}
