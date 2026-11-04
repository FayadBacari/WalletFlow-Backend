namespace WalletFlow.Application.UserAccounts;

// Résultat d'une confirmation d'adresse : acceptée, ou refusée avec la raison du refus.
public class AccountConfirmationResult
{
    public string? LinkErrorMessage { get; init; }
    public string? PasswordErrorMessage { get; init; }

    public bool IsConfirmed => LinkErrorMessage == null && PasswordErrorMessage == null;
}
