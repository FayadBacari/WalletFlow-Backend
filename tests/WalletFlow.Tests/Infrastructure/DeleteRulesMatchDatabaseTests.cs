using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WalletFlow.Infrastructure.Persistence;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie que chaque clé étrangère du modèle EF Core suit la règle de suppression de la vraie base (MPD).
public class DeleteRulesMatchDatabaseTests
{
    [Fact]
    public async Task EveryForeignKeyFollowsTheDeleteRuleOfTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();

        var databaseRules = await ReadDatabaseDeleteRulesAsync(databaseContext);
        var modelRules = ReadModelDeleteRules(databaseContext);

        Assert.Equal(databaseRules, modelRules);
    }

    // Lit le catalogue de PostgreSQL et renvoie « table.colonne code » pour chaque clé étrangère.
    private static async Task<List<string>> ReadDatabaseDeleteRulesAsync(WalletFlowDatabaseContext databaseContext)
    {
        var rules = await databaseContext.Database
            .SqlQuery<string>($"""
                SELECT c.conrelid::regclass::text || '.' || a.attname || ' ' || c.confdeltype::text AS "Value"
                FROM pg_constraint c
                JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
                WHERE c.contype = 'f'
                """)
            .ToListAsync();

        rules.Sort(StringComparer.Ordinal);
        return rules;
    }

    // Lit le modèle EF Core et renvoie « table.colonne code » pour chaque clé étrangère.
    private static List<string> ReadModelDeleteRules(WalletFlowDatabaseContext databaseContext)
    {
        var rules = new List<string>();

        foreach (var entityType in databaseContext.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName()!;
            var storeObject = StoreObjectIdentifier.Table(tableName, null);

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                var columnName = foreignKey.Properties[0].GetColumnName(storeObject);
                var code = ToDatabaseCode(foreignKey.DeleteBehavior);

                rules.Add(tableName + "." + columnName + " " + code);
            }
        }

        rules.Sort(StringComparer.Ordinal);
        return rules;
    }

    // Donne le code que PostgreSQL utilise pour cette règle de suppression.
    private static string ToDatabaseCode(DeleteBehavior deleteBehavior)
    {
        switch (deleteBehavior)
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
                throw new InvalidOperationException("Règle de suppression non prévue par le MPD : " + deleteBehavior);
        }
    }
}
