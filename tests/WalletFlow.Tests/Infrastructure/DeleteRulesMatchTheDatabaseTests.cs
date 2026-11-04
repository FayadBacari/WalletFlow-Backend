using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WalletFlow.Infrastructure.Persistence;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie que chaque clé étrangère du modèle EF Core suit la règle de suppression de la vraie base (MPD).
public class DeleteRulesMatchTheDatabaseTests
{
    [Fact]
    public async Task EveryForeignKeyFollowsTheDeleteRuleOfTheRealDatabase()
    {
        await using var databaseContextOfThisDeleteRulesTest = CreateDatabaseContextConnectedToLocalDatabase();

        var deleteRulesFoundInTheRealDatabase = await ReadDeleteRulesFoundInTheRealDatabaseAsync(databaseContextOfThisDeleteRulesTest);
        var deleteRulesDeclaredInTheEntityModel = ReadDeleteRulesDeclaredInTheEntityModel(databaseContextOfThisDeleteRulesTest);

        Assert.Equal(deleteRulesFoundInTheRealDatabase, deleteRulesDeclaredInTheEntityModel);
    }

    // Lit le catalogue de PostgreSQL et renvoie « table.colonne code » pour chaque clé étrangère.
    private static async Task<List<string>> ReadDeleteRulesFoundInTheRealDatabaseAsync(WalletFlowDatabaseContext databaseContextOfThisDeleteRulesTest)
    {
        var deleteRulesReadInTheDatabaseCatalog = await databaseContextOfThisDeleteRulesTest.Database
            .SqlQuery<string>($"""
                SELECT c.conrelid::regclass::text || '.' || a.attname || ' ' || c.confdeltype::text AS "Value"
                FROM pg_constraint c
                JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
                WHERE c.contype = 'f'
                """)
            .ToListAsync();

        deleteRulesReadInTheDatabaseCatalog.Sort(StringComparer.Ordinal);
        return deleteRulesReadInTheDatabaseCatalog;
    }

    // Lit le modèle EF Core et renvoie « table.colonne code » pour chaque clé étrangère.
    private static List<string> ReadDeleteRulesDeclaredInTheEntityModel(WalletFlowDatabaseContext databaseContextOfThisDeleteRulesTest)
    {
        var deleteRulesReadInTheEntityModel = new List<string>();

        foreach (var entityTypeMappedByEntityFrameworkCore in databaseContextOfThisDeleteRulesTest.Model.GetEntityTypes())
        {
            var nameOfTheTableMappedToThisEntityType = entityTypeMappedByEntityFrameworkCore.GetTableName()!;
            var tableIdentifierUsedToReadTheColumnNames = StoreObjectIdentifier.Table(nameOfTheTableMappedToThisEntityType, null);

            foreach (var foreignKeyOfTheEntityTypeBeingRead in entityTypeMappedByEntityFrameworkCore.GetForeignKeys())
            {
                var nameOfTheForeignKeyColumnInTheTable = foreignKeyOfTheEntityTypeBeingRead.Properties[0].GetColumnName(tableIdentifierUsedToReadTheColumnNames);
                var databaseCodeOfTheDeleteRule = ConvertDeleteBehaviorIntoItsDatabaseCode(foreignKeyOfTheEntityTypeBeingRead.DeleteBehavior);

                deleteRulesReadInTheEntityModel.Add(nameOfTheTableMappedToThisEntityType + "." + nameOfTheForeignKeyColumnInTheTable + " " + databaseCodeOfTheDeleteRule);
            }
        }

        deleteRulesReadInTheEntityModel.Sort(StringComparer.Ordinal);
        return deleteRulesReadInTheEntityModel;
    }

    // Donne le code que PostgreSQL utilise pour cette règle de suppression.
    private static string ConvertDeleteBehaviorIntoItsDatabaseCode(DeleteBehavior deleteBehaviorOfTheForeignKeyBeingRead)
    {
        switch (deleteBehaviorOfTheForeignKeyBeingRead)
        {
            case DeleteBehavior.Cascade:
                return "c";
            case DeleteBehavior.SetNull:
                return "n";
            case DeleteBehavior.NoAction:
                return "a";

            // EF Core ne touche pas à la clé : c'est le ON DELETE SET NULL de la base qui s'applique.
            case DeleteBehavior.ClientNoAction:
                return "n";

            default:
                throw new InvalidOperationException("Règle de suppression non prévue par le MPD : " + deleteBehaviorOfTheForeignKeyBeingRead);
        }
    }
}
