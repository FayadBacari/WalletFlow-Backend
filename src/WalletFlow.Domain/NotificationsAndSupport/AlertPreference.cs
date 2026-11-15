namespace WalletFlow.Domain.NotificationsAndSupport;

// Choix d'un utilisateur pour un type d'alerte : activée ou non. Sans choix enregistré, une alerte est activée (DD §9.2).
// Correspond à la table parametrer du MPD.
public class AlertPreference
{
    private bool isEnabled = true;

    // id_utilisateur
    public Guid UserId { get; init; }

    // code_type_alerte
    public AlertKind Kind { get; init; }

    // alerte_activee : vrai par défaut, et toujours vrai pour une alerte de sécurité.
    public bool IsEnabled
    {
        get => isEnabled;
        init => isEnabled = value;
    }

    // Crée le premier choix de l'utilisateur pour ce type d'alerte.
    public static AlertPreference Create(Guid userId, AlertType type, bool enabled)
    {
        ThrowIfSecurityAlertIsDisabled(type, enabled);

        return new AlertPreference { UserId = userId, Kind = type.Kind, IsEnabled = enabled };
    }

    // Active ou désactive ce type d'alerte. Une alerte de sécurité ne se désactive pas (GR Notif) : la base le refuse aussi
    // (tg_parametrer_controle).
    public void Switch(AlertType type, bool enabled)
    {
        if (type.Kind != Kind)
        {
            throw new ArgumentException("Ce type d'alerte n'est pas celui du choix.", nameof(type));
        }

        ThrowIfSecurityAlertIsDisabled(type, enabled);
        isEnabled = enabled;
    }

    // Refuse de désactiver un type d'alerte qui ne se désactive pas.
    private static void ThrowIfSecurityAlertIsDisabled(AlertType type, bool enabled)
    {
        if (!enabled && !type.CanBeDisabled)
        {
            throw new InvalidOperationException("Une alerte de sécurité ne peut pas être désactivée.");
        }
    }
}
