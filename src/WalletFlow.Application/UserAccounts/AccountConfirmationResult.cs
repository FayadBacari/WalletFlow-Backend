namespace WalletFlow.Application.UserAccounts;

// Résultat d'une confirmation d'adresse : une seule de ces valeurs à la fois.
public enum AccountConfirmationResult
{
    AccountConfirmed,
    LinkNoLongerValid,
    WrongPassword
}
