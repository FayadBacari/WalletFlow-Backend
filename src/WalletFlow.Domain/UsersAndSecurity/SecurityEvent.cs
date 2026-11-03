using System.Net;

namespace WalletFlow.Domain.UsersAndSecurity;

// Ligne du journal de sécurité.
// Correspond à la table evenement_securite du MPD (DD §4.5). Les dates sont en UTC.
public class SecurityEvent
{
    // id_evenement
    public Guid Id { get; set; } = Guid.NewGuid();

    // type_evenement
    public SecurityEventType Type { get; set; }

    // resultat_evenement
    public SecurityEventResult Result { get; set; }

    // date_evenement : l'événement est conservé de 6 mois à 1 an.
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // adresse_ip_evenement : effacée quand le compte de l'utilisateur est supprimé.
    public IPAddress? IpAddress { get; set; }

    // id_utilisateur : vide pour une connexion échouée avec une adresse inconnue,
    // ou quand le compte de l'utilisateur a été supprimé.
    public Guid? UserId { get; set; }
}
