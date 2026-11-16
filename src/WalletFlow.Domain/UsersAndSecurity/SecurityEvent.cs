using System.Net;

namespace WalletFlow.Domain.UsersAndSecurity;

// Ligne du journal de sécurité.
// Correspond à la table evenement_securite du MPD (DD §4.5). Les dates sont en UTC.
public class SecurityEvent
{
    // id_evenement
    public Guid Id { get; init; } = Guid.NewGuid();

    // type_evenement
    public SecurityEventType Type { get; init; }

    // resultat_evenement
    public SecurityEventResult Result { get; init; }

    // date_evenement : l'événement est conservé 1 an, puis effacé (SS §11).
    // Donnée par l'horloge de l'application : le compilateur refuse un événement créé sans elle.
    public required DateTime OccurredAt { get; init; }

    // adresse_ip_evenement : effacée quand le compte de l'utilisateur est supprimé.
    public IPAddress? IpAddress { get; init; }

    // id_utilisateur : vide pour une connexion échouée avec une adresse inconnue,
    // ou quand le compte de l'utilisateur a été supprimé.
    public Guid? UserId { get; init; }

    // id_utilisateur_vise : seulement pour une action de l'administrateur, le compte sur lequel il agit (SS §8, §11).
    // Vide quand ce compte est effacé.
    public Guid? TargetUserId { get; init; }
}
