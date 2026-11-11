using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit l'usage d'un lien envoyé par e-mail en code de la colonne type_jeton, et inversement.
public static class EmailTokenTypeDatabaseCodes
{
    // Code de la colonne type_jeton pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<EmailTokenType> Table =
        new EnumDatabaseCodes<EmailTokenType>(
            new Dictionary<EmailTokenType, string>
            {
                { EmailTokenType.SignupConfirmation, "confirmation_inscription" },
                { EmailTokenType.PasswordRecovery, "recuperation_mdp" },
                { EmailTokenType.AccountUnlock, "deblocage" },
                { EmailTokenType.EmailChange, "changement_email" },
            });
}
