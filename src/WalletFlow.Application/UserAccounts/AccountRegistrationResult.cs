namespace WalletFlow.Application.UserAccounts;

// Résultat d'une demande d'inscription : toujours une seule des formes écrites dans ce fichier.
// Il ne dit jamais si l'adresse était déjà utilisée (SS §2).
public abstract class AccountRegistrationResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AccountRegistrationResult()
    {
    }

    // La demande est acceptée : un e-mail est parti, que l'adresse soit libre ou déjà utilisée.
    public sealed class Accepted : AccountRegistrationResult
    {
    }

    // La demande ne respecte pas les règles. Les deux refus peuvent arriver ensemble.
    public sealed class RefusedByRules : AccountRegistrationResult
    {
        public required bool IsEmailInvalid { get; init; }
        public required bool IsPasswordInvalid { get; init; }
    }

    // Trop de demandes pour cette adresse, qu'un compte existe ou non (SS §6).
    public sealed class TooManyRequests : AccountRegistrationResult
    {
    }

    // L'e-mail n'a pas pu partir. Une panne touche toutes les adresses de la même façon (SS §2).
    public sealed class EmailCouldNotBeSent : AccountRegistrationResult
    {
        // Compte concerné par l'e-mail qui n'a pas pu partir. Il sert seulement au journal technique (SS §7).
        public required Guid? UserId { get; init; }
    }
}
