namespace WalletFlow.Application.AccountSettings;

// Résultat de la confirmation d'une nouvelle adresse.
public enum EmailChangeConfirmationResult
{
    EmailChanged,
    LinkNoLongerValid,
    WrongPassword,
    EmailAlreadyUsed
}
