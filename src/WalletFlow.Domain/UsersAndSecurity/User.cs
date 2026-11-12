namespace WalletFlow.Domain.UsersAndSecurity;

// Personne inscrite : un utilisateur ou un administrateur.
// Correspond à la table utilisateur du MPD (DD §3). Les dates sont en UTC.
// Chaque valeur se donne à la création (« init »). Ensuite, seules les règles écrites dans cette classe
// changent l'état du compte : aucun autre code ne peut le mettre dans un état interdit.
public class User
{
    private string email = string.Empty;
    private string? lastName = null;
    private string? firstName = null;
    private string passwordHash = string.Empty;
    private bool isEmailConfirmed = false;
    private bool isTwoFactorEnabled = false;
    private string? encryptedTotpSecret = null;
    private short failedLoginAttemptCount = 0;
    private short lockoutCount = 0;
    private DateTime? lockoutEndsAt = null;

    // id_utilisateur
    public Guid Id { get; init; } = Guid.NewGuid();

    // email_utilisateur : adresse de connexion, toujours en minuscules.
    public string Email
    {
        get => email;
        init => email = value;
    }

    // mot_de_passe_hash : empreinte Argon2id du mot de passe, jamais le mot de passe lui-même.
    public string PasswordHash
    {
        get => passwordHash;
        init => passwordHash = value;
    }

    // nom_utilisateur : facultatif.
    public string? LastName
    {
        get => lastName;
        init => lastName = value;
    }

    // prenom_utilisateur : facultatif.
    public string? FirstName
    {
        get => firstName;
        init => firstName = value;
    }

    // role_utilisateur : utilisateur par défaut, administrateur seulement depuis le serveur.
    public UserRole Role { get; init; } = UserRole.User;

    // email_confirme : le compte n'est actif qu'une fois l'adresse confirmée.
    public bool IsEmailConfirmed
    {
        get => isEmailConfirmed;
        init => isEmailConfirmed = value;
    }

    // double_auth_activee
    public bool IsTwoFactorEnabled
    {
        get => isTwoFactorEnabled;
        init => isTwoFactorEnabled = value;
    }

    // secret_totp_chiffre : rempli seulement si la double authentification est activée.
    public string? EncryptedTotpSecret
    {
        get => encryptedTotpSecret;
        init => encryptedTotpSecret = value;
    }

    // canal_notification : e-mail par défaut.
    public NotificationChannel NotificationChannel { get; init; } = NotificationChannel.Email;

    // nb_tentatives_echouees : de 0 à 5.
    public short FailedLoginAttemptCount
    {
        get => failedLoginAttemptCount;
        init => failedLoginAttemptCount = value;
    }

    // nb_blocages : sert à doubler la durée de chaque nouveau blocage.
    public short LockoutCount
    {
        get => lockoutCount;
        init => lockoutCount = value;
    }

    // date_fin_blocage : vide si le compte n'est pas bloqué.
    public DateTime? LockoutEndsAt
    {
        get => lockoutEndsAt;
        init => lockoutEndsAt = value;
    }

    // date_demande_suppression : vide s'il n'y a pas de demande de suppression.
    public DateTime? DeletionRequestedAt { get; init; }

    // Renvoie vrai si le compte est bloqué à cette date. Le blocage finit à sa date de fin exacte.
    public bool IsLockedAt(DateTime now)
    {
        return lockoutEndsAt != null && lockoutEndsAt > now;
    }

    // Compte un essai de plus, avant même de vérifier le mot de passe : des requêtes simultanées
    // ne peuvent donc pas tester plus de 5 mots de passe. Le dernier essai bloque aussitôt le compte :
    // si le serveur s'arrête ensuite, le blocage a quand même une date de fin.
    // Renvoie vrai si c'est cet essai qui bloque le compte.
    public bool CountLoginAttempt(DateTime now)
    {
        // Le blocage précédent est terminé : le compte retrouve ses 5 essais.
        if (failedLoginAttemptCount >= AccountLockoutRules.MaximumFailedAttempts)
        {
            failedLoginAttemptCount = 0;
            lockoutEndsAt = null;
        }

        failedLoginAttemptCount = (short)(failedLoginAttemptCount + 1);

        if (failedLoginAttemptCount < AccountLockoutRules.MaximumFailedAttempts)
        {
            return false;
        }

        if (lockoutCount < AccountLockoutRules.MaximumLockoutCount)
        {
            lockoutCount = (short)(lockoutCount + 1);
        }

        lockoutEndsAt = now + AccountLockoutRules.CalculateLockoutDuration(lockoutCount);
        return true;
    }

    // Termine le blocage en cours et redonne ses 5 essais au compte : lien de déblocage ou récupération réussie (GR Auth).
    // Le nombre de blocages ne change pas : il ne repart à zéro qu'après une connexion réussie.
    public void Unlock()
    {
        failedLoginAttemptCount = 0;
        lockoutEndsAt = null;
    }

    // Remet tout à zéro après une connexion réussie (GR Auth).
    public void ResetLoginCounters()
    {
        failedLoginAttemptCount = 0;
        lockoutCount = 0;
        lockoutEndsAt = null;
    }

    // Note que le propriétaire a ouvert le lien de confirmation : le compte devient actif (GR Auth).
    public void ConfirmEmail()
    {
        isEmailConfirmed = true;
    }

    // Remplace l'empreinte du mot de passe. Une empreinte vide ne correspondrait à aucun mot de passe : elle est refusée.
    public void ReplacePasswordHash(string newHash)
    {
        if (string.IsNullOrEmpty(newHash))
        {
            throw new ArgumentException("L'empreinte du nouveau mot de passe est vide.", nameof(newHash));
        }

        passwordHash = newHash;
    }

    // Remplace le nom et le prénom (GR Compte). Un nom vide (null) est effacé.
    // Ils arrivent déjà sous leur forme enregistrée : une autre forme, ou un nom qui ne respecte pas les règles, est refusé
    // (voir PersonalNameRules). Un texte fait seulement d'espaces, par exemple, aurait dû devenir null.
    public void ReplaceName(string? newLastName, string? newFirstName)
    {
        if (!IsValidSavedName(newLastName) || !IsValidSavedName(newFirstName))
        {
            throw new ArgumentException("Le nom ou le prénom n'a pas sa forme enregistrée, ou ne respecte pas les règles du dictionnaire de données.");
        }

        lastName = newLastName;
        firstName = newFirstName;
    }

    // Renvoie vrai si le nom est déjà sous sa forme enregistrée et respecte les règles.
    private static bool IsValidSavedName(string? name)
    {
        return name == PersonalNameRules.Normalize(name)
            && PersonalNameRules.IsValid(name);
    }

    // Remplace l'adresse de connexion par la nouvelle adresse, une fois confirmée (GR Compte).
    // Elle arrive déjà sous sa forme enregistrée, en minuscules : une autre forme est refusée (voir EmailAddressRules).
    public void ReplaceEmail(string newEmail)
    {
        if (newEmail != EmailAddressRules.Normalize(newEmail) || !EmailAddressRules.IsValid(newEmail))
        {
            throw new ArgumentException("La nouvelle adresse e-mail n'a pas le format enregistré en base.", nameof(newEmail));
        }

        email = newEmail;
    }

    // Active la double authentification avec son secret chiffré : l'un ne va jamais sans l'autre (DD §3).
    public void ActivateTwoFactor(string encryptedSecret)
    {
        if (string.IsNullOrEmpty(encryptedSecret))
        {
            throw new ArgumentException("Le secret chiffré de la double authentification est vide.", nameof(encryptedSecret));
        }

        isTwoFactorEnabled = true;
        encryptedTotpSecret = encryptedSecret;
    }

    // Désactive la double authentification et oublie son secret : l'un ne reste jamais sans l'autre (DD §3).
    public void DeactivateTwoFactor()
    {
        isTwoFactorEnabled = false;
        encryptedTotpSecret = null;
    }
}
