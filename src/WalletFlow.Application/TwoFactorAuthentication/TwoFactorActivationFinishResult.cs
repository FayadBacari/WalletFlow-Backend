namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat de la fin d'une activation : toujours une seule des formes écrites dans ce fichier.
// Des classes plutôt que des records : un record afficherait les codes si on l'écrivait dans un journal.
public abstract class TwoFactorActivationFinishResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private TwoFactorActivationFinishResult()
    {
    }

    // La double authentification est activée : les codes de secours ne sont donnés que cette fois.
    public sealed class Activated : TwoFactorActivationFinishResult
    {
        public required List<string> BackupCodes { get; init; }
    }

    // Le premier code n'est pas bon : le propriétaire peut encore essayer.
    public sealed class WrongCode : TwoFactorActivationFinishResult
    {
    }

    // Le secret en attente n'existe plus, ou le compte a changé : il faut recommencer depuis le début.
    public sealed class ActivationMustRestart : TwoFactorActivationFinishResult
    {
    }
}
