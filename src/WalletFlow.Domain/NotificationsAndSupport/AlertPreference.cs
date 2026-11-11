namespace WalletFlow.Domain.NotificationsAndSupport;

// Choix d'un utilisateur pour un type d'alerte : activée ou non.
// Correspond à la table parametrer du MPD (DD §9.2).
public class AlertPreference
{
    // id_utilisateur
    public Guid UserId { get; init; }

    // code_type_alerte
    public string AlertTypeCode { get; init; } = string.Empty;

    // alerte_activee : vrai par défaut, et toujours vrai pour une alerte de sécurité.
    public bool IsEnabled { get; init; } = true;
}
