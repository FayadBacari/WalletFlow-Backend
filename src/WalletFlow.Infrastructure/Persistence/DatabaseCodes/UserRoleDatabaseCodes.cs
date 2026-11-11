using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit le rôle d'un utilisateur en code de la colonne role_utilisateur, et inversement.
public static class UserRoleDatabaseCodes
{
    // Code de la colonne role_utilisateur pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<UserRole> Table =
        new EnumDatabaseCodes<UserRole>(
            new Dictionary<UserRole, string>
            {
                { UserRole.User, "utilisateur" },
                { UserRole.Administrator, "administrateur" },
            });
}
