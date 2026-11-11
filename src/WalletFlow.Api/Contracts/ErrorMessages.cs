namespace WalletFlow.Api.Contracts;

// Les phrases de refus que l'API montre au visiteur. Les cas d'utilisation disent seulement ce qui s'est passé :
// c'est ici, à la frontière HTTP, que chaque résultat reçoit sa phrase.
public static class ErrorMessages
{
    // Le même message quelle que soit la raison du refus : il ne dit jamais si le compte existe (GR Auth).
    public const string WrongCredentials = "Identifiants incorrects";

    public const string EmailNotConfirmed =
        "Ton adresse e-mail n'est pas encore confirmée. Ouvre le lien reçu par e-mail, ou inscris-toi à nouveau pour en recevoir un autre.";

    public const string InvalidEmailAddress = "L'adresse e-mail n'est pas valide.";

    public const string PasswordBreaksRules =
        "Le mot de passe doit contenir de 12 à 128 caractères, dont une majuscule, une minuscule, un chiffre et un caractère spécial, sans caractère invisible.";

    public const string ConfirmationLinkNoLongerValid = "Ce lien n'est plus valable. Inscris-toi à nouveau pour en recevoir un autre.";

    public const string WrongConfirmationPassword = "Ce n'est pas le mot de passe choisi à l'inscription. Si tu l'as oublié, inscris-toi à nouveau.";

    public const string UnlockLinkNoLongerValid = "Ce lien n'est plus valable. Si ton compte est encore bloqué, utilise « Mot de passe oublié ».";

    public const string RecoveryLinkNoLongerValid = "Ce lien n'est plus valable. Demande un nouveau lien avec « Mot de passe oublié ».";

    public const string WrongActivationPassword = "Ce mot de passe n'est pas le bon.";

    public const string TwoFactorAlreadyEnabled = "La double authentification est déjà activée.";

    public const string ActivationMustRestart = "Cette activation n'est plus valable. Recommence depuis le début.";

    public const string WrongTwoFactorCode = "Ce code n'est pas bon. Vérifie-le, ou attends le code suivant.";

    // Le même message pour un mauvais mot de passe et pour un mauvais code : une session volée ne peut pas servir à tester des mots de passe un par un.
    public const string WrongPasswordOrCode = "Le mot de passe ou le code n'est pas bon.";

    public const string TwoFactorNotEnabled = "La double authentification n'est pas activée.";

    public const string LoginMustRestart = "Cette connexion n'est plus valable. Reconnecte-toi.";
}
