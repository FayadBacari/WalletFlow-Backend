namespace WalletFlow.Application.AccountRecovery;

// Résultat du choix d'un nouveau mot de passe : accepté, ou refusé avec la raison du refus.
public class PasswordResetResult
{
    public string? LinkErrorMessage { get; init; }
    public string? PasswordErrorMessage { get; init; }

    public bool IsPasswordChanged => LinkErrorMessage == null && PasswordErrorMessage == null;
}
