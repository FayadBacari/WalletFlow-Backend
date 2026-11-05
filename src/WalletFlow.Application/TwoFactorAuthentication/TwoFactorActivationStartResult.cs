namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat du début d'une activation : le secret à donner à l'application, ou le refus à afficher.
// Une classe plutôt qu'un record : un record afficherait le secret si on l'écrivait dans un journal.
public class TwoFactorActivationStartResult
{
    public string? WrongPasswordErrorMessage { get; init; }
    public string? AlreadyEnabledErrorMessage { get; init; }
    public string? SecretOfTheAuthenticatorApplication { get; init; }
    public string? LinkThatAddsTheSecretToTheApplication { get; init; }

    public bool IsActivationStarted => SecretOfTheAuthenticatorApplication != null;
}
