namespace WalletFlow.Application.UserSessions;

// Résultat d'une connexion : réussie, en attente du code de double authentification, ou refusée avec le message à afficher.
// Une classe plutôt qu'un record : un record afficherait les jetons si on l'écrivait dans un journal.
public class AccountLoginResult
{
    public string? WrongCredentialsErrorMessage { get; init; }
    public string? EmailAddressNotConfirmedErrorMessage { get; init; }
    public string? SessionTokenToPutInTheCookie { get; init; }
    public string? DeviceTokenToPutInTheCookie { get; init; }
    public string? WaitingLoginTokenToPutInTheCookie { get; init; }

    public bool IsLoggedIn => SessionTokenToPutInTheCookie != null;
    public bool IsWaitingForTwoFactorCode => WaitingLoginTokenToPutInTheCookie != null;
}
