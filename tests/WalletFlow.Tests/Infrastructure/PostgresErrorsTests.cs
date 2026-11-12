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

    // Une clé étrangère vers une ligne qu'une autre requête peut supprimer au même instant est un conflit, quelle que soit
    // la forme de l'erreur : une clé différée est vérifiée à la validation, et PostgreSQL l'envoie alors seule.
    [Fact]
    public void OnlyTheKeysOfRowsDeletedMeanwhileAreConflicts()
    {
        var categoryGoneAtCommit = new PostgresException("refus de test", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation,
            constraintName: PostgresErrors.TransactionCategoryForeignKey);

        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.BankAccountConnectionForeignKey)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.TransactionAccountForeignKey)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.RuleCategoryForeignKey)));
        Assert.True(PostgresErrors.IsConflict(categoryGoneAtCommit));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, "suggestion_id_categorie_fkey")));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.BankAccountConnectionForeignKey)));
    }

    // Les unicités qu'une autre requête peut prendre au même instant sont des conflits ; toute autre erreur reste une panne.
    [Fact]
    public void OnlyTheUniquenessTakenMeanwhileIsAConflict()
    {
        Assert.True(PostgresErrors.IsEmailAlreadyUsed(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.EmailUniqueConstraint)));
        Assert.False(PostgresErrors.IsEmailAlreadyUsed(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.CategoryNameUniqueIndex)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.EmailUniqueConstraint)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.CategoryNameUniqueIndex)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.TransactionExternalIdUniqueConstraint)));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, "ux_categorie_autres")));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.CheckViolation, "ck_transaction_montant")));
        Assert.False(PostgresErrors.IsConflict(new InvalidOperationException()));
        Assert.True(PostgresErrors.IsConflict(new InvalidOperationException("Erreur passagère.", CreateSaveError(PostgresErrorCodes.DeadlockDetected))));
    }
}
