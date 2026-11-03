namespace WalletFlow.Domain.NotificationsAndSupport;

// État d'une demande de support (statut_demande, DD §10.1).
public enum SupportRequestStatus
{
    Sent,       // envoyee
    InProgress, // en_cours
    Resolved    // resolue
}
