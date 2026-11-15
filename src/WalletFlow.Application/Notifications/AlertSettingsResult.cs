namespace WalletFlow.Application.Notifications;

// Résultat d'un changement des réglages d'alerte : toujours une seule des formes écrites dans ce fichier.
public abstract class AlertSettingsResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AlertSettingsResult()
    {
    }

    // Le réglage est enregistré : voici les réglages à jour.
    public sealed class Saved : AlertSettingsResult
    {
        public required AlertSettings Settings { get; init; }
    }

    // Une alerte de sécurité ne peut pas être désactivée (GR Notif).
    public sealed class CannotBeDisabled : AlertSettingsResult
    {
    }

    // Le compte n'existe plus.
    public sealed class AccountGone : AlertSettingsResult
    {
    }

    // D'autres requêtes changent le compte sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : AlertSettingsResult
    {
    }
}
