namespace WalletFlow.Application.UserAccounts;

// Résultat d'une demande d'inscription : acceptée, ou refusée avec la raison de chaque refus.
// Il ne dit jamais si l'adresse était déjà utilisée (SS §2).
public class AccountRegistrationResult
{
    public string? EmailAddressErrorMessage { get; init; }
    public string? PasswordErrorMessage { get; init; }

    public bool IsAccepted => EmailAddressErrorMessage == null && PasswordErrorMessage == null;
}
