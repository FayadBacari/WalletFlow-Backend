namespace WalletFlow.Application.AccountRecovery;

// Résultat d'une demande de récupération : toujours une seule des formes écrites dans ce fichier.
// Il ne dit jamais si un compte utilise l'adresse (SS §2).
public abstract class PasswordRecoveryRequestResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private PasswordRecoveryRequestResult()
    {
    }

    // La demande est prise : un e-mail partira en arrière-plan si un compte utilise l'adresse.
    public sealed class RequestAccepted : PasswordRecoveryRequestResult
    {
    }

    // L'adresse n'a pas un format valide.
    public sealed class InvalidEmailAddressFormat : PasswordRecoveryRequestResult
    {
    }

    // Cette adresse a déjà fait trop de demandes dans l'heure (SS §6).
    public sealed class TooManyRequests : PasswordRecoveryRequestResult
    {
    }
}
