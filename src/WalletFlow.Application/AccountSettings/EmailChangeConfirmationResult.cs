namespace WalletFlow.Application.AccountSettings;

// Résultat de la confirmation d'une nouvelle adresse : toujours une seule des formes écrites dans ce fichier.
public abstract class EmailChangeConfirmationResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private EmailChangeConfirmationResult()
    {
    }

    // La nouvelle adresse remplace l'ancienne, qui sera prévenue.
    public sealed class EmailChanged : EmailChangeConfirmationResult
    {
    }

    // Le lien n'existe pas, a déjà servi, a expiré ou a été remplacé.
    public sealed class LinkNoLongerValid : EmailChangeConfirmationResult
    {
    }

    // Le mot de passe n'est pas le bon : le lien reste valable.
    public sealed class WrongPassword : EmailChangeConfirmationResult
    {
    }

    // Un autre compte a pris cette adresse depuis la demande.
    public sealed class EmailAlreadyUsed : EmailChangeConfirmationResult
    {
    }
}
