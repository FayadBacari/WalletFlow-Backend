namespace WalletFlow.Application.UserSessions;

// Résultat d'une connexion : toujours une seule des formes écrites dans ce fichier.
// Chaque forme porte seulement ce dont elle a besoin : aucun champ n'est facultatif.
// Des classes plutôt que des records : un record afficherait les jetons si on l'écrivait dans un journal.
public abstract class AccountLoginResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AccountLoginResult()
    {
    }

    // La connexion a réussi : les deux jetons vont dans les cookies.
    public sealed class LoggedIn : AccountLoginResult
    {
        public required string SessionToken { get; init; }
        public required string DeviceToken { get; init; }
    }

    // Le mot de passe est bon, mais le compte demande aussi son code de double authentification.
    public sealed class WaitingForTwoFactorCode : AccountLoginResult
    {
        public required string WaitingLoginToken { get; init; }
    }

    // Refus générique : il ne dit jamais ce qui était faux, ni si le compte existe (GR Auth).
    public sealed class WrongCredentials : AccountLoginResult
    {
    }

    // Le mot de passe est bon, mais l'adresse e-mail n'est pas encore confirmée.
    public sealed class EmailAddressNotConfirmed : AccountLoginResult
    {
    }
}
