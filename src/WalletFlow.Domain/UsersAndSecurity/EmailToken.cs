namespace WalletFlow.Domain.UsersAndSecurity;

// Lien à usage unique envoyé par e-mail : seule l'empreinte du jeton est stockée.
// Correspond à la table jeton du MPD (DD §4.2). Les dates sont en UTC.
public class EmailToken
{
    // id_jeton
    public Guid Id { get; set; } = Guid.NewGuid();

    // type_jeton
    public EmailTokenType Type { get; set; }

    // empreinte_jeton : empreinte SHA-256 du jeton, jamais le jeton lui-même.
    public string TokenHash { get; set; } = string.Empty;

    // date_expiration_jeton
    public DateTime ExpiresAt { get; set; }

    // date_utilisation_jeton : vide tant que le lien n'a pas servi.
    public DateTime? UsedAt { get; set; }

    // nouvel_email : seulement pour un changement d'adresse e-mail.
    public string? NewEmail { get; set; }

    // id_utilisateur
    public Guid UserId { get; set; }
}
