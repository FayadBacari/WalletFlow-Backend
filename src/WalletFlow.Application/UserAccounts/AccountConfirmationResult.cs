namespace WalletFlow.Application.UserAccounts;

// Résultat d'une confirmation d'adresse : toujours une seule des formes écrites dans ce fichier.
public abstract class AccountConfirmationResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AccountConfirmationResult()
    {
    }

    // Le compte est confirmé : il est actif.
    public sealed class AccountConfirmed : AccountConfirmationResult
    {
    }

    // Le lien n'existe pas, a déjà servi, a expiré ou a été remplacé.
    public sealed class LinkNoLongerValid : AccountConfirmationResult
    {
    }

    // Le mot de passe n'est pas celui choisi à l'inscription : le lien reste valable.
    public sealed class WrongPassword : AccountConfirmationResult
    {
    }
}
