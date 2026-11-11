namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat du début d'une activation : toujours une seule des formes écrites dans ce fichier.
// Des classes plutôt que des records : un record afficherait le secret si on l'écrivait dans un journal.
public abstract class TwoFactorActivationStartResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private TwoFactorActivationStartResult()
    {
    }

    // L'activation commence : le secret est à donner à l'application d'authentification.
    public sealed class Started : TwoFactorActivationStartResult
    {
        public required string Secret { get; init; }
        public required string SetupLink { get; init; }
    }

    // Le mot de passe donné n'est pas celui du compte.
    public sealed class WrongPassword : TwoFactorActivationStartResult
    {
    }

    // La double authentification est déjà activée sur ce compte.
    public sealed class AlreadyEnabled : TwoFactorActivationStartResult
    {
    }
}
