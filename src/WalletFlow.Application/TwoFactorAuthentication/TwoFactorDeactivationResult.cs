namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat d'une désactivation : toujours une seule des formes écrites dans ce fichier.
// Un mauvais mot de passe et un mauvais code donnent la même forme :
// une session volée ne peut pas servir à tester des mots de passe un par un.
public abstract class TwoFactorDeactivationResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private TwoFactorDeactivationResult()
    {
    }

    // La double authentification est désactivée, et ses codes de secours effacés.
    public sealed class TwoFactorDeactivated : TwoFactorDeactivationResult
    {
    }

    // La double authentification n'était pas activée.
    public sealed class TwoFactorNotEnabled : TwoFactorDeactivationResult
    {
    }

    // Le mot de passe ou le code n'est pas le bon : le même refus pour les deux.
    public sealed class WrongPasswordOrCode : TwoFactorDeactivationResult
    {
    }
}
