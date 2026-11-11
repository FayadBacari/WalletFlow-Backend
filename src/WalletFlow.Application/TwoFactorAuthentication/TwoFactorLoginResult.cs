namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat de la fin d'une connexion : toujours une seule des formes écrites dans ce fichier.
// Des classes plutôt que des records : un record afficherait les jetons si on l'écrivait dans un journal.
public abstract class TwoFactorLoginResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private TwoFactorLoginResult()
    {
    }

    // Le code est bon : les deux jetons vont dans les cookies.
    public sealed class LoggedIn : TwoFactorLoginResult
    {
        public required string SessionToken { get; init; }
        public required string DeviceToken { get; init; }
    }

    // Le code n'est pas bon : le visiteur peut encore essayer.
    public sealed class WrongCode : TwoFactorLoginResult
    {
    }

    // La connexion en attente n'existe plus : le visiteur doit redonner son mot de passe.
    public sealed class LoginMustRestart : TwoFactorLoginResult
    {
    }
}
