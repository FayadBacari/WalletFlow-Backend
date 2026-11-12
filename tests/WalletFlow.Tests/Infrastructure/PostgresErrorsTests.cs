using Microsoft.EntityFrameworkCore;
using Npgsql;
using WalletFlow.Infrastructure.Persistence;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les erreurs de PostgreSQL que l'application traite comme des conflits, à rejouer : toutes les autres restent des pannes.
public class PostgresErrorsTests
{
    // Crée l'erreur qu'EF Core lève quand PostgreSQL refuse l'écriture avec ce code et cette contrainte.
    private static DbUpdateException CreateSaveError(string sqlState, string? constraintName = null)
    {
        var postgresException = new PostgresException("refus de test", "ERROR", "ERROR", sqlState, constraintName: constraintName);

        return new DbUpdateException("Enregistrement refusé.", postgresException);
    }

    // PostgreSQL arrête une transaction pour débloquer un interblocage : elle peut être rejouée. EF Core donne l'erreur d'un
    // enregistrement dans une InvalidOperationException, autour de la DbUpdateException, comme dans la vraie base
    // (BankConnectionRepositoryTests.DeadlockDuringASaveIsAConflict).
    [Fact]
    public void DeadlockIsRecognizedWrappedOrNot()
    {
        var deadlock = new PostgresException("interblocage", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected);
        var transientError = new InvalidOperationException("Erreur passagère.", CreateSaveError(PostgresErrorCodes.DeadlockDetected));

        Assert.True(PostgresErrors.IsDeadlock(deadlock));
        Assert.True(PostgresErrors.IsDeadlock(CreateSaveError(PostgresErrorCodes.DeadlockDetected)));
        Assert.True(PostgresErrors.IsDeadlock(transientError));
        Assert.False(PostgresErrors.IsDeadlock(CreateSaveError(PostgresErrorCodes.UniqueViolation)));
        Assert.False(PostgresErrors.IsDeadlock(new InvalidOperationException("Erreur passagère.", CreateSaveError(PostgresErrorCodes.SerializationFailure))));
        Assert.False(PostgresErrors.IsDeadlock(new InvalidOperationException()));
    }

    // Seule la clé qui relie un compte bancaire à sa connexion compte : une autre clé étrangère reste une panne.
    [Fact]
    public void OnlyTheBankAccountConnectionKeyMeansTheConnectionIsGone()
    {
        Assert.True(PostgresErrors.IsBankConnectionGone(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.BankAccountConnectionForeignKey)));
        Assert.False(PostgresErrors.IsBankConnectionGone(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, "transaction_id_categorie_fkey")));
        Assert.False(PostgresErrors.IsBankConnectionGone(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.BankAccountConnectionForeignKey)));
    }

    [Fact]
    public void UniqueViolationsAreRecognizedByTheirConstraint()
    {
        Assert.True(PostgresErrors.IsEmailAlreadyUsed(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.EmailUniqueConstraint)));
        Assert.True(PostgresErrors.IsCategoryNameAlreadyUsed(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.CategoryNameUniqueIndex)));
        Assert.False(PostgresErrors.IsCategoryNameAlreadyUsed(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.EmailUniqueConstraint)));
    }
}
