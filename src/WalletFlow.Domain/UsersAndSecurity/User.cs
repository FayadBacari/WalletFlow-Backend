namespace WalletFlow.Domain.UsersAndSecurity;

// Personne inscrite : un utilisateur ou un administrateur.
// Correspond à la table utilisateur du MPD (DD §3). Les dates sont en UTC.
public class User
{
    // id_utilisateur
    public Guid Id { get; set; } = Guid.NewGuid();

    // email_utilisateur : adresse de connexion, toujours en minuscules.
    public string Email { get; set; } = string.Empty;

    // mot_de_passe_hash : empreinte Argon2id du mot de passe, jamais le mot de passe lui-même.
    public string PasswordHash { get; set; } = string.Empty;

    // nom_utilisateur : facultatif.
    public string? LastName { get; set; }

    // prenom_utilisateur : facultatif.
    public string? FirstName { get; set; }

    // role_utilisateur : utilisateur par défaut, administrateur seulement depuis le serveur.
    public UserRole Role { get; set; } = UserRole.User;

    // email_confirme : le compte n'est actif qu'une fois l'adresse confirmée.
    public bool IsEmailConfirmed { get; set; } = false;

    // double_auth_activee
    public bool IsTwoFactorEnabled { get; set; } = false;

    // secret_totp_chiffre : rempli seulement si la double authentification est activée.
    public string? EncryptedTotpSecret { get; set; }

    // canal_notification : e-mail par défaut.
    public NotificationChannel NotificationChannel { get; set; } = NotificationChannel.Email;

    // nb_tentatives_echouees : de 0 à 5.
    public short FailedLoginAttemptCount { get; set; } = 0;

    // nb_blocages : sert à doubler la durée de chaque nouveau blocage.
    public short LockoutCount { get; set; } = 0;

    // date_fin_blocage : vide si le compte n'est pas bloqué.
    public DateTime? LockoutEndsAt { get; set; }

    // date_demande_suppression : vide s'il n'y a pas de demande de suppression.
    public DateTime? DeletionRequestedAt { get; set; }
}
