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
    private DateTime? deletionRequestedAt = null;
    private DateOnly? lastSuggestionMonth = null;
    private long? lastAuthenticatorStep = null;
    private NotificationChannel notificationChannel = NotificationChannel.Email;

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
    public NotificationChannel NotificationChannel
    {
        get => notificationChannel;
        init => notificationChannel = value;
    }

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
    public DateTime? DeletionRequestedAt
    {
        get => deletionRequestedAt;
        init => deletionRequestedAt = value;
    }

    // dernier_mois_suggestions : premier jour du dernier mois dont les suggestions ont été calculées, vide avant le premier calcul.
    public DateOnly? LastSuggestionMonth
    {
        get => lastSuggestionMonth;
        init => lastSuggestionMonth = value;
    }

    // dernier_pas_totp : pas de temps (30 secondes) du dernier code de l'application accepté, vide sans double authentification.
    // Un code de ce pas, ou d'un pas plus ancien, ne sert plus (SS §2) : même après un redémarrage, même sur un autre serveur.
    public long? LastAuthenticatorStep
    {
        get => lastAuthenticatorStep;
        init => lastAuthenticatorStep = value;
    }

    // Renvoie vrai si un code de l'application de ce pas de temps peut servir : la double authentification est activée, et
    // ce pas est plus récent que celui du dernier code accepté.
    public bool CanUseAuthenticatorStep(long step)
    {
        return isTwoFactorEnabled && step > 0 && (lastAuthenticatorStep == null || step > lastAuthenticatorStep);
    }

    // Note qu'un code de ce pas de temps vient de servir : ni lui ni aucun code plus ancien ne serviront plus. Un code qui ne peut
    // pas servir est refusé : il ne doit jamais être accepté une seconde fois.
    public void UseAuthenticatorStep(long step)
    {
        if (!CanUseAuthenticatorStep(step))
        {
            throw new InvalidOperationException("Ce code de l'application a déjà servi, ou un code plus récent a servi.");
        }

        lastAuthenticatorStep = step;
    }

    // Renvoie vrai si le compte est bloqué à cette date. Le blocage finit à sa date de fin exacte.
    public bool IsLockedAt(DateTime now)
    {
        return lockoutEndsAt != null && lockoutEndsAt > now;
    }

    // Compte un essai de plus. La connexion ne donne le résultat d'un mot de passe qu'une fois son essai compté : des requêtes
    // simultanées ne peuvent donc pas tester plus de 5 mots de passe. Le dernier essai bloque aussitôt le compte :
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

    // Note que les suggestions de ce mois sont calculées : elles ne le seront plus (GR Sacs). Le mois est un premier jour de mois,
    // après le dernier mois noté : un autre jour, ou un retour en arrière, est refusé.
    public void NoteSuggestionMonth(DateOnly month)
    {
        if (month.Day != 1 || (lastSuggestionMonth is DateOnly noted && month <= noted))
        {
            throw new ArgumentException("Le mois n'est pas un premier jour de mois, ou il n'est pas après le dernier mois noté.", nameof(month));
        }

        lastSuggestionMonth = month;
    }

    // Garde le canal choisi pour recevoir les alertes (GR Notif). Tant que l'application mobile n'existe pas, toutes les alertes
    // partent quand même par e-mail : le choix du téléphone est gardé pour plus tard. Une valeur hors de la liste est refusée.
    public void ChangeNotificationChannel(NotificationChannel newChannel)
    {
        if (!Enum.IsDefined(newChannel))
        {
            throw new ArgumentOutOfRangeException(nameof(newChannel), "Ce canal n'est pas dans la liste.");
        }

        notificationChannel = newChannel;
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

    // Active la double authentification avec son secret chiffré : l'un ne va jamais sans l'autre (DD §3). Le premier code, qui
    // vient de prouver que l'application est bien réglée, est noté comme utilisé : il ne pourra pas resservir pour se connecter.
    public void ActivateTwoFactor(string encryptedSecret, long firstStep)
    {
        if (string.IsNullOrEmpty(encryptedSecret))
        {
            throw new ArgumentException("Le secret chiffré de la double authentification est vide.", nameof(encryptedSecret));
        }

        if (firstStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstStep), "Le pas de temps du premier code doit être positif.");
        }

        isTwoFactorEnabled = true;
        encryptedTotpSecret = encryptedSecret;
        lastAuthenticatorStep = firstStep;
    }

    // Remplace le secret chiffré par le même secret, chiffré avec une autre clé (SS §7). Le secret lui-même ne change pas :
    // le pas du dernier code reste valable. Sans double authentification, il n'y a pas de secret à remplacer.
    public void ReplaceEncryptedTotpSecret(string newEncryptedSecret)
    {
        if (!isTwoFactorEnabled)
        {
            throw new InvalidOperationException("La double authentification n'est pas activée : il n'y a pas de secret à remplacer.");
        }

        if (string.IsNullOrEmpty(newEncryptedSecret))
        {
            throw new ArgumentException("Le nouveau secret chiffré est vide.", nameof(newEncryptedSecret));
        }

        encryptedTotpSecret = newEncryptedSecret;
    }

    // Désactive la double authentification et oublie son secret, avec le pas de son dernier code : l'un ne reste jamais sans
    // l'autre (DD §3), et un nouveau secret repart sans passé.
    public void DeactivateTwoFactor()
    {
        isTwoFactorEnabled = false;
        encryptedTotpSecret = null;
        lastAuthenticatorStep = null;
    }

    // Demande la suppression du compte à cette date : il sera effacé 30 jours après, sauf si l'utilisateur se reconnecte
    // avant (GR Suppression). Une demande déjà en cours ne repart pas à zéro.
    public void RequestDeletion(DateTime now)
    {
        if (deletionRequestedAt != null)
        {
            throw new InvalidOperationException("La suppression de ce compte est déjà demandée.");
        }

        deletionRequestedAt = now;
    }

    // Annule la demande de suppression : une connexion réussie pendant les 30 jours rouvre le compte (GR Suppression).
    public void CancelDeletion()
    {
        deletionRequestedAt = null;
    }

    // Renvoie vrai si la suppression du compte est demandée depuis 30 jours ou plus : il peut être effacé.
    public bool IsDueForErasureAt(DateTime now)
    {
        return deletionRequestedAt != null && AccountDeletionRules.FindErasureMoment(deletionRequestedAt.Value) <= now;
    }
}
