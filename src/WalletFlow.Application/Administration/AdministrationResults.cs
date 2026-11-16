namespace WalletFlow.Application.Administration;

// Ce que donne la vérification d'une route d'administration (SS §4, §8).
public enum AdministratorAccess
{
    // L'utilisateur est administrateur, avec la double authentification activée.
    Granted,

    // L'utilisateur n'est pas administrateur.
    NotAdministrator,

    // L'administrateur n'a pas activé la double authentification : il doit le faire avant d'utiliser l'administration.
    TwoFactorRequired
}

// Résultat de la recherche d'un compte par son adresse : toujours une seule des formes écrites ici.
public abstract class AdminAccountSearchResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AdminAccountSearchResult()
    {
    }

    // Un compte utilise cette adresse.
    public sealed class Found : AdminAccountSearchResult
    {
        public required AdminAccountView Account { get; init; }
    }

    // Aucun compte n'utilise cette adresse.
    public sealed class NotFound : AdminAccountSearchResult
    {
    }

    // L'adresse tapée n'a pas un format valide : aucune recherche n'est faite.
    public sealed class EmailBreaksRules : AdminAccountSearchResult
    {
    }
}

// Résultat d'un déblocage par l'administrateur : toujours une seule des formes écrites ici.
public abstract class AdminAccountUnlockResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AdminAccountUnlockResult()
    {
    }

    // Le compte est débloqué.
    public sealed class Unlocked : AdminAccountUnlockResult
    {
        public required AdminAccountView Account { get; init; }
    }

    // Le compte n'existe pas, ou plus.
    public sealed class NotFound : AdminAccountUnlockResult
    {
    }

    // Le compte n'est pas bloqué : rien n'est changé.
    public sealed class NotLocked : AdminAccountUnlockResult
    {
    }

    // D'autres requêtes changent le compte sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : AdminAccountUnlockResult
    {
    }
}

// Résultat de l'envoi du lien qui désactive la double authentification : toujours une seule des formes écrites ici.
public abstract class AdminTwoFactorLinkResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AdminTwoFactorLinkResult()
    {
    }

    // Le lien entre dans la file d'e-mails, vers l'adresse du compte.
    public sealed class Sent : AdminTwoFactorLinkResult
    {
    }

    // Le compte n'existe pas, ou plus.
    public sealed class NotFound : AdminTwoFactorLinkResult
    {
    }

    // La double authentification du compte n'est pas activée : il n'y a rien à désactiver.
    public sealed class TwoFactorNotEnabled : AdminTwoFactorLinkResult
    {
    }

    // Le mot de passe du compte a changé il y a moins de 7 jours : le lien ne marcherait pas. C'est justement le signe d'un voleur
    // de la boîte mail, qui vient de réinitialiser le mot de passe (SS §2).
    public sealed class PasswordChangedRecently : AdminTwoFactorLinkResult
    {
    }

    // D'autres requêtes changent le compte sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : AdminTwoFactorLinkResult
    {
    }
}

// Résultat d'une réponse de l'administrateur à une demande : toujours une seule des formes écrites ici.
public abstract class AdminSupportAnswerResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AdminSupportAnswerResult()
    {
    }

    // La réponse est enregistrée, et son e-mail entre dans la file. La demande est « en cours ».
    public sealed class Answered : AdminSupportAnswerResult
    {
        public required AdminSupportRequestDetails Request { get; init; }
    }

    // La demande n'existe pas, ou plus.
    public sealed class NotFound : AdminSupportAnswerResult
    {
    }

    // Le texte de la réponse ne respecte pas la règle du texte (SS §5).
    public sealed class ContentBreaksRules : AdminSupportAnswerResult
    {
    }

    // D'autres requêtes changent la demande sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : AdminSupportAnswerResult
    {
    }
}

// Résultat d'une demande marquée « résolue » : toujours une seule des formes écrites ici.
public abstract class AdminSupportResolutionResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AdminSupportResolutionResult()
    {
    }

    // La demande est résolue.
    public sealed class Resolved : AdminSupportResolutionResult
    {
        public required AdminSupportRequestDetails Request { get; init; }
    }

    // La demande n'existe pas, ou plus.
    public sealed class NotFound : AdminSupportResolutionResult
    {
    }

    // La demande est déjà résolue : rien n'est changé.
    public sealed class AlreadyResolved : AdminSupportResolutionResult
    {
    }

    // D'autres requêtes changent la demande sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : AdminSupportResolutionResult
    {
    }
}
