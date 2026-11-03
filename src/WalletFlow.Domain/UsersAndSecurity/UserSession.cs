namespace WalletFlow.Domain.UsersAndSecurity;

// Session ouverte par un utilisateur, gérée côté serveur.
// Correspond à la table session du MPD (DD §4.4). Les dates sont en UTC.
public class UserSession
{
    // id_session : valeur aléatoire d'au moins 128 bits, créée à chaque connexion.
    public string Id { get; set; } = string.Empty;

    // date_derniere_activite : la session expire après 30 minutes sans activité.
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    // id_utilisateur
    public Guid UserId { get; set; }
}
