using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit l'usage d'un lien envoyé par e-mail en code de la colonne type_jeton, et inversement.
public static class EmailTokenTypeDatabaseCodes
{
    // Convertisseur utilisé par EF Core pour lire et écrire la colonne type_jeton.
    public static readonly ValueConverter<EmailTokenType, string> ConverterBetweenEmailTokenTypeAndDatabaseCode =
        new ValueConverter<EmailTokenType, string>(
            emailTokenTypeToWriteIntoTheDatabase => ConvertEmailTokenTypeIntoItsDatabaseCode(emailTokenTypeToWriteIntoTheDatabase),
            databaseCodeReadForTheEmailTokenType => ConvertDatabaseCodeIntoItsEmailTokenType(databaseCodeReadForTheEmailTokenType));

    // Donne le code enregistré en base pour un usage de lien.
    public static string ConvertEmailTokenTypeIntoItsDatabaseCode(EmailTokenType emailTokenTypeToConvertIntoDatabaseCode)
    {
        switch (emailTokenTypeToConvertIntoDatabaseCode)
        {
            case EmailTokenType.SignupConfirmation:
                return "confirmation_inscription";
            case EmailTokenType.PasswordRecovery:
                return "recuperation_mdp";
            case EmailTokenType.AccountUnlock:
                return "deblocage";
            case EmailTokenType.EmailChange:
                return "changement_email";
            default:
                throw new ArgumentOutOfRangeException(nameof(emailTokenTypeToConvertIntoDatabaseCode));
        }
    }

    // Donne l'usage de lien qui correspond à un code lu en base.
    public static EmailTokenType ConvertDatabaseCodeIntoItsEmailTokenType(string databaseCodeToConvertIntoEmailTokenType)
    {
        switch (databaseCodeToConvertIntoEmailTokenType)
        {
            case "confirmation_inscription":
                return EmailTokenType.SignupConfirmation;
            case "recuperation_mdp":
                return EmailTokenType.PasswordRecovery;
            case "deblocage":
                return EmailTokenType.AccountUnlock;
            case "changement_email":
                return EmailTokenType.EmailChange;
            default:
                throw new ArgumentOutOfRangeException(nameof(databaseCodeToConvertIntoEmailTokenType));
        }
    }
}
