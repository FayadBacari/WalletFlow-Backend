namespace WalletFlow.Application.UserAccounts;

// Résultat d'une demande d'inscription : acceptée, ou refusée avec la raison du refus.
// Il ne dit jamais si l'adresse était déjà utilisée (SS §2).
public class AccountRegistrationResult
{
    public string? EmailAddressErrorMessage { get; init; }
    public string? PasswordErrorMessage { get; init; }

    // Trop de demandes pour cette adresse, qu'un compte existe ou non (SS §6).
    public bool TooManyRequestsForThisEmailAddress { get; init; }

    // L'e-mail n'a pas pu partir. Une panne touche toutes les adresses de la même façon (SS §2).
    public bool EmailCouldNotBeSent { get; init; }

    // Compte concerné par l'e-mail qui n'a pas pu partir. Il sert seulement au journal technique (SS §7).
    public Guid? UserIdentifierForTheTechnicalLog { get; init; }

    public bool IsAccepted =>
        EmailAddressErrorMessage == null
        && PasswordErrorMessage == null
        && !TooManyRequestsForThisEmailAddress
        && !EmailCouldNotBeSent;
}
