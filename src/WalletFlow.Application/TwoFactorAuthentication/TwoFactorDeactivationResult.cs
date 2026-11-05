namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat d'une désactivation : réussie, ou refusée avec le message à afficher.
public class TwoFactorDeactivationResult
{
    public string? WrongPasswordOrCodeErrorMessage { get; init; }
    public string? NotEnabledErrorMessage { get; init; }

    public bool IsTwoFactorDeactivated => WrongPasswordOrCodeErrorMessage == null && NotEnabledErrorMessage == null;
}
