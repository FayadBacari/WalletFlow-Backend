using System.Reflection;
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
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.MovementSourceBagForeignKey)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.MovementDestinationBagForeignKey)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.SuggestionCategoryForeignKey)));
        Assert.True(PostgresErrors.IsConflict(categoryGoneAtCommit));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, "mouvement_sac_id_suggestion_fkey")));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, "sac_id_utilisateur_fkey")));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, "suggestion_id_utilisateur_fkey")));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.BankAccountConnectionForeignKey)));
    }

    // GR Suppression : le compte qu'une connexion, un lien ou un administrateur vient de lire peut être effacé au même instant.
    // Son nouvel appareil, sa session, son événement du journal et sa demande d'e-mail butent alors sur leur clé : c'est un
    // conflit. Une clé vers le compte qu'aucune requête sans session n'écrit reste une panne.
    [Fact]
    public void KeysOfAnAccountErasedMeanwhileAreConflicts()
    {
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.DeviceAccountForeignKey)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.SessionAccountForeignKey)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.SecurityEventAccountForeignKey)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, PostgresErrors.EmailRequestAccountForeignKey)));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, "code_secours_id_utilisateur_fkey")));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.SessionAccountForeignKey)));
    }

    // Chaque contrainte et chaque index que l'application reconnaît existe dans la vraie base : un nom mal écrit ne reconnaîtrait
    // jamais son conflit, et la requête tomberait en panne au lieu de recommencer.
    [Fact]
    public async Task EveryNameExistsInTheRealDatabase()
    {
        var names = typeof(PostgresErrors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();

        var databaseNames = await databaseContext.Database
            .SqlQuery<string>($"""
                SELECT conname::text AS "Value" FROM pg_constraint
                UNION
                SELECT relname::text FROM pg_class WHERE relkind = 'i'
                """)
            .ToListAsync();

        Assert.Equal(28, names.Count);
        Assert.All(names, name => Assert.Contains(name, databaseNames));
    }

    // Une clé étrangère précise est reconnue à n'importe quelle profondeur de la chaîne d'erreurs, comme le reste.
    [Fact]
    public void ForeignKeyIsFoundAtAnyDepth()
    {
        var journalKey = "evenement_securite_id_utilisateur_fkey";
        var wrappedTwice = new InvalidOperationException("Erreur passagère.", CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, journalKey));

        Assert.True(PostgresErrors.IsForeignKeyViolation(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, journalKey), journalKey));
        Assert.True(PostgresErrors.IsForeignKeyViolation(wrappedTwice, journalKey));
        Assert.False(PostgresErrors.IsForeignKeyViolation(CreateSaveError(PostgresErrorCodes.ForeignKeyViolation, "autre_fkey"), journalKey));
        Assert.False(PostgresErrors.IsForeignKeyViolation(CreateSaveError(PostgresErrorCodes.UniqueViolation, journalKey), journalKey));
        Assert.False(PostgresErrors.IsForeignKeyViolation(new InvalidOperationException(), journalKey));
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
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.SuggestionCategoryMonthUniqueIndex)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.SuggestionRuleMonthUniqueIndex)));
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.MovementSuggestionUniqueConstraint)));
        Assert.Equal("mouvement_sac_id_suggestion_key", PostgresErrors.MovementSuggestionUniqueConstraint);
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, "ux_categorie_autres")));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.CheckViolation, "ck_transaction_montant")));
        Assert.False(PostgresErrors.IsConflict(new InvalidOperationException()));
        Assert.True(PostgresErrors.IsConflict(new InvalidOperationException("Erreur passagère.", CreateSaveError(PostgresErrorCodes.DeadlockDetected))));
    }

    // Un mouvement vidé de ses deux sacs par deux suppressions simultanées est un conflit (BagRepositoryTests le provoque
    // dans la vraie base). Seule cette contrainte l'est : toute autre contrainte refusée reste une panne.
    [Fact]
    public void OnlyTheMovementEmptiedMeanwhileIsAConflict()
    {
        Assert.Equal("ck_mouvement_un_sac", PostgresErrors.MovementWithOneBagCheck);
        Assert.True(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.CheckViolation, PostgresErrors.MovementWithOneBagCheck)));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.CheckViolation, "ck_mouvement_montant")));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.CheckViolation, "ck_mouvement_sacs_differents")));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.UniqueViolation, PostgresErrors.MovementWithOneBagCheck)));
        Assert.False(PostgresErrors.IsConflict(CreateSaveError(PostgresErrorCodes.CheckViolation)));
    }
}
