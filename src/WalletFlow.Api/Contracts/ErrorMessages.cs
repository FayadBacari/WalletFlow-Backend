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

    // Le mot de passe actuel, redemandé avant une action sensible (SS §2), ou à la confirmation d'une nouvelle adresse.
    public const string WrongCurrentPassword = "Ce mot de passe n'est pas le bon.";

    public const string TwoFactorAlreadyEnabled = "La double authentification est déjà activée.";

    public const string ActivationMustRestart = "Cette activation n'est plus valable. Recommence depuis le début.";

    public const string WrongTwoFactorCode = "Ce code n'est pas bon. Vérifie-le, ou attends le code suivant.";

    // Le même message pour un mauvais mot de passe et pour un mauvais code : une session volée ne peut pas servir à tester des mots de passe un par un.
    public const string WrongPasswordOrCode = "Le mot de passe ou le code n'est pas bon.";

    public const string TwoFactorNotEnabled = "La double authentification n'est pas activée.";

    public const string LoginMustRestart = "Cette connexion n'est plus valable. Reconnecte-toi.";

    public const string PersonalNameBreaksRules =
        "Ce champ contient de 1 à 50 caractères, sans caractère invisible. Laisse-le vide pour l'effacer.";

    public const string AccountChangedMeanwhile = "Ton compte vient de changer. Réessaie.";

    public const string SameEmailAddress = "C'est déjà l'adresse de ton compte.";

    public const string EmailChangeLinkNoLongerValid =
        "Ce lien n'est plus valable. Demande un nouveau changement d'adresse depuis ton compte.";

    // Seul le propriétaire de la boîte mail, qui a aussi le mot de passe du compte, peut lire ce message (voir EmailChangeConfirmationService).
    public const string EmailAlreadyUsed = "Cette adresse est déjà utilisée par un autre compte.";

    public const string CategoryNameBreaksRules = "Le nom d'une catégorie contient de 1 à 50 caractères, sans caractère invisible.";

    public const string CategoryNameAlreadyUsed = "Tu as déjà une catégorie qui porte ce nom.";

    // Le nombre écrit ici est CategoryRules.MaximumCountPerUser : un test vérifie qu'ils restent égaux.
    public const string TooManyCategories = "Tu as déjà 100 catégories, le maximum. Supprimes-en une pour en créer une autre.";

    public const string OtherCategoryIsFixed = "La catégorie « Autres » ne peut être ni renommée ni supprimée.";

    // Les montants écrits ici sont ceux de CategoryRules, en euros : 1 centime et 1 000 000 000 centimes.
    public const string CategoryBudgetBreaksRules = "Le budget mensuel va de 0,01 € à 10 000 000 €.";

    public const string CategoriesChangedMeanwhile = "Tes catégories viennent de changer. Réessaie.";

    public const string BankNotOffered = "Cette banque n'est pas proposée.";

    public const string BankServiceUnavailable = "Le service de connexion bancaire ne répond pas. Réessaie plus tard.";

    // Le même message pour un lien inconnu, expiré, déjà utilisé, ou commencé par un autre compte (SS §7).
    public const string BankLinkNoLongerValid = "Cette connexion bancaire n'est plus valable. Recommence depuis WalletFlow.";

    public const string BankRefusedAuthorization = "La banque n'a pas donné son autorisation. Recommence depuis WalletFlow.";

    public const string NoBankAccountAdded =
        "Aucun compte n'a pu être ajouté : WalletFlow garde seulement les comptes en euros, et chacun une seule fois.";

    public const string BankConnectionsChangedMeanwhile = "Tes connexions bancaires viennent de changer. Réessaie.";
}
