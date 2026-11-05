namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat de la fin d'une connexion : réussie, mauvais code, ou connexion à recommencer.
// Une classe plutôt qu'un record : un record afficherait les jetons si on l'écrivait dans un journal.
public class TwoFactorLoginResult
{
    public string? WrongCodeErrorMessage { get; init; }
    public string? LoginMustRestartErrorMessage { get; init; }
    public string? SessionTokenToPutInTheCookie { get; init; }
    public string? DeviceTokenToPutInTheCookie { get; init; }

    public bool IsLoggedIn => SessionTokenToPutInTheCookie != null;
}
