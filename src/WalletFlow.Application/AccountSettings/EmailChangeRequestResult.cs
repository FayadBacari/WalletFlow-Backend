namespace WalletFlow.Application.AccountSettings;

// Résultat d'une demande de changement d'adresse : toujours une seule des formes écrites dans ce fichier.
// Une demande acceptée ne dit jamais si la nouvelle adresse était libre (SS §2).
public abstract class EmailChangeRequestResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private EmailChangeRequestResult()
    {
    }

    // La demande est prise : les e-mails partiront en arrière-plan.
    public sealed class RequestAccepted : EmailChangeRequestResult
    {
    }

    // La nouvelle adresse n'a pas un format valide.
    public sealed class InvalidEmailAddress : EmailChangeRequestResult
    {
    }

    // La nouvelle adresse est déjà celle du compte.
    public sealed class SameEmailAddress : EmailChangeRequestResult
    {
    }

    // Le mot de passe n'est pas le bon, sur un compte sans double authentification.
    public sealed class WrongPassword : EmailChangeRequestResult
    {
    }

    // Le mot de passe ou le code n'est pas le bon, avec la double authentification : le même refus pour les deux.
    public sealed class WrongPasswordOrCode : EmailChangeRequestResult
    {
    }

    // La nouvelle adresse a déjà reçu trop de demandes dans l'heure (SS §6).
    public sealed class TooManyRequests : EmailChangeRequestResult
    {
    }
}
