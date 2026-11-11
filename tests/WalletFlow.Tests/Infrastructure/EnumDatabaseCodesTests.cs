using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les refus de la traduction commune entre une liste du code et les codes d'une colonne.
public class EnumDatabaseCodesTests
{
    [Fact]
    public void TableOfCodesThatForgetsOneValueIsRefusedWhenItIsBuilt()
    {
        Dictionary<UserRole, string> codesWithoutAdministrator = new Dictionary<UserRole, string>
        {
            { UserRole.User, "utilisateur" },
        };

        Assert.Throws<InvalidOperationException>(
            () => new EnumDatabaseCodes<UserRole>(codesWithoutAdministrator));
    }

    [Fact]
    public void TableOfCodesThatGivesTheSameCodeTwiceIsRefusedWhenItIsBuilt()
    {
        Dictionary<UserRole, string> codesWithDuplicate = new Dictionary<UserRole, string>
        {
            { UserRole.User, "utilisateur" },
            { UserRole.Administrator, "utilisateur" },
        };

        Assert.Throws<InvalidOperationException>(
            () => new EnumDatabaseCodes<UserRole>(codesWithDuplicate));
    }

    [Fact]
    public void TableOfCodesThatGivesAnEmptyCodeIsRefusedWhenItIsBuilt()
    {
        Dictionary<UserRole, string> codesWithEmptyCode = new Dictionary<UserRole, string>
        {
            { UserRole.User, "utilisateur" },
            { UserRole.Administrator, " " },
        };

        Assert.Throws<InvalidOperationException>(
            () => new EnumDatabaseCodes<UserRole>(codesWithEmptyCode));
    }

    [Fact]
    public void DatabaseCodeUnknownInTheTableIsRefusedInsteadOfGivingADefaultValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UserRoleDatabaseCodes.Table.ToValue("super_administrateur"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UserRoleDatabaseCodes.Table.ToValue("Administrateur"));
    }

    [Fact]
    public void ValueOutsideTheListIsRefusedInsteadOfBeingWrittenIntoTheDatabase()
    {
        UserRole unknownRole = (UserRole)999;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => UserRoleDatabaseCodes.Table.ToCode(unknownRole));
    }
}
