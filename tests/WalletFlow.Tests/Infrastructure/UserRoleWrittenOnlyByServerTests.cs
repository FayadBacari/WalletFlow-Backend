using Microsoft.EntityFrameworkCore;
using Npgsql;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Le rôle administrateur ne s'attribue que depuis le serveur, jamais depuis l'application (SS §8 et §14).
// Ces tests passent par l'utilisateur de base de l'API, comme l'application elle-même.
public class UserRoleWrittenOnlyByServerTests
{
    [Fact]
    public async Task NewAccountReceivesTheDefaultRoleFromTheDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var newAccount = new User { Email = "test-role-par-defaut@exemple.test", PasswordHash = "empreinte-de-test" };

        databaseContext.Users.Add(newAccount);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        var reloadedAccount = await databaseContext.Users.SingleAsync(row => row.Id == newAccount.Id);
        Assert.Equal(UserRole.User, reloadedAccount.Role);
    }

    [Fact]
    public async Task DatabaseRefusesNewAccountCreatedAsAdministrator()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var newAccount = new User { Email = "test-role-admin@exemple.test", PasswordHash = "empreinte-de-test", Role = UserRole.Administrator };

        databaseContext.Users.Add(newAccount);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => databaseContext.SaveChangesAsync());

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgresException.SqlState);
    }

    [Fact]
    public async Task RoleOfAnExistingAccountCannotBeChangedByTheCode()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var existingAccount = new User { Email = "test-role-change@exemple.test", PasswordHash = "empreinte-de-test" };
        databaseContext.Users.Add(existingAccount);
        await databaseContext.SaveChangesAsync();

        // Le code ne peut plus changer le rôle après la création (« init ») : seul EF Core pourrait encore l'écrire.
        databaseContext.Entry(existingAccount).Property(nameof(User.Role)).CurrentValue = UserRole.Administrator;

        await Assert.ThrowsAsync<InvalidOperationException>(() => databaseContext.SaveChangesAsync());
    }

    // Même si EF Core laissait passer la modification, la base la refuserait : l'application n'a pas ce droit.
    [Fact]
    public async Task DatabaseRefusesRoleChangeSentDirectlyInSql()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();

        var postgresException = await Assert.ThrowsAsync<PostgresException>(() =>
            databaseContext.Database.ExecuteSqlRawAsync("UPDATE utilisateur SET role_utilisateur = 'administrateur'"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgresException.SqlState);
    }
}
