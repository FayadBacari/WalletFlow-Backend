using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit le rôle d'un utilisateur en code de la colonne role_utilisateur, et inversement.
public static class UserRoleDatabaseCodes
{
    // Convertisseur utilisé par EF Core pour lire et écrire la colonne role_utilisateur.
    public static readonly ValueConverter<UserRole, string> ConverterBetweenUserRoleAndDatabaseCode =
        new ValueConverter<UserRole, string>(
            userRoleToWriteIntoTheDatabase => ConvertUserRoleIntoItsDatabaseCode(userRoleToWriteIntoTheDatabase),
            databaseCodeReadForTheUserRole => ConvertDatabaseCodeIntoItsUserRole(databaseCodeReadForTheUserRole));

    // Donne le code enregistré en base pour un rôle.
    public static string ConvertUserRoleIntoItsDatabaseCode(UserRole userRoleToConvertIntoDatabaseCode)
    {
        switch (userRoleToConvertIntoDatabaseCode)
        {
            case UserRole.User:
                return "utilisateur";
            case UserRole.Administrator:
                return "administrateur";
            default:
                throw new ArgumentOutOfRangeException(nameof(userRoleToConvertIntoDatabaseCode));
        }
    }

    // Donne le rôle qui correspond à un code lu en base.
    public static UserRole ConvertDatabaseCodeIntoItsUserRole(string databaseCodeToConvertIntoUserRole)
    {
        switch (databaseCodeToConvertIntoUserRole)
        {
            case "utilisateur":
                return UserRole.User;
            case "administrateur":
                return UserRole.Administrator;
            default:
                throw new ArgumentOutOfRangeException(nameof(databaseCodeToConvertIntoUserRole));
        }
    }
}
