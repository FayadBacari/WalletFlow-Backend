namespace WalletFlow.Application.AccountRecovery;

// Résultat du choix d'un nouveau mot de passe : toujours une seule des formes écrites dans ce fichier.
public abstract class PasswordResetResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private PasswordResetResult()
    {
    }

    // Le nouveau mot de passe est enregistré, et toutes les sessions du compte sont fermées.
    public sealed class PasswordChanged : PasswordResetResult
    {
    }

    // Le lien n'existe pas, a déjà servi, a expiré ou a été remplacé.
    public sealed class LinkNoLongerValid : PasswordResetResult
    {
    }

    // Le nouveau mot de passe ne respecte pas les règles : le lien reste valable.
    public sealed class PasswordBreaksRules : PasswordResetResult
    {
    }
}
