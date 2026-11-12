using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.TableConfigurations;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie que le modèle EF Core utilise exactement les tables et les colonnes du script SQL,
// sans en oublier et sans en inventer. Aucun serveur PostgreSQL n'est nécessaire.
public class DatabaseModelMatchesSqlScriptTests
{
    [Fact]
    public void EntityModelUsesExactlyTheTablesAndColumnsOfTheSqlScript()
    {
        var scriptColumns = ReadScriptColumns();
        var modelColumns = ReadModelColumns();

        Assert.Equal(scriptColumns, modelColumns);
    }

    // Lit les instructions CREATE TABLE du script SQL et renvoie « table.colonne » pour chaque colonne.
    private static List<string> ReadScriptColumns()
    {
        var scriptPath =
            Path.Combine(AppContext.BaseDirectory, "Database", "001_create_walletflow_schema.sql");
        var scriptText = File.ReadAllText(scriptPath);
        var columnsByTable = new List<string>();

        foreach (Match statement in
                 Regex.Matches(scriptText, @"CREATE TABLE (\w+) \((.*?)\n\);", RegexOptions.Singleline))
        {
            var tableName = statement.Groups[1].Value;
            var statementBody = statement.Groups[2].Value;

            foreach (Match columnDeclaration in
                     Regex.Matches(statementBody,
                         @"^    ([a-z0-9_]+) +(UUID|VARCHAR|CHAR|INTEGER|SMALLINT|DATE|TIMESTAMPTZ|BOOLEAN|INET)",
                         RegexOptions.Multiline))
            {
                columnsByTable.Add(
                    tableName + "." + columnDeclaration.Groups[1].Value);
            }
        }

        columnsByTable.Sort();
        return columnsByTable;
    }

    // Construit le modèle EF Core et renvoie « table.colonne » pour chaque propriété enregistrée.
    private static List<string> ReadModelColumns()
    {
        // Le modèle se construit sans se connecter : le serveur indiqué ici n'est jamais contacté.
        var options = new DbContextOptionsBuilder<WalletFlowDatabaseContext>()
            .UseNpgsql("Host=localhost;Database=modele_sans_connexion")
            .Options;
        using var databaseContext = new WalletFlowDatabaseContext(options);
        var columnsByTable = new List<string>();

        foreach (var entityType in databaseContext.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName()!;
            var storeObject = StoreObjectIdentifier.Table(tableName, null);

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);

                // xmin est une colonne système de PostgreSQL : toute table l'a, sans que le script la déclare.
                if (columnName == RowVersionConfiguration.ColumnName)
                {
                    continue;
                }

                columnsByTable.Add(tableName + "." + columnName);
            }
        }

        columnsByTable.Sort();
        return columnsByTable;
    }

    // Les lignes que l'application lit puis modifie ont leur version, et c'est le seul contrôle de concurrence du modèle :
    // une écriture vérifie que personne n'a changé la ligne depuis sa lecture, quelle que soit la colonne.
    [Fact]
    public void OnlyTheRowVersionControlsConcurrentWrites()
    {
        var options = new DbContextOptionsBuilder<WalletFlowDatabaseContext>()
            .UseNpgsql("Host=localhost;Database=modele_sans_connexion")
            .Options;
        using var databaseContext = new WalletFlowDatabaseContext(options);
        var concurrencyColumns = new List<string>();

        foreach (var entityType in databaseContext.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName()!;
            var storeObject = StoreObjectIdentifier.Table(tableName, null);

            foreach (var property in entityType.GetProperties())
            {
                if (property.IsConcurrencyToken)
                {
                    concurrencyColumns.Add(tableName + "." + property.GetColumnName(storeObject) + " " + property.GetColumnType());
                }
            }
        }

        concurrencyColumns.Sort();
        Assert.Equal(
            new List<string> { "categorie.xmin xid", "code_secours.xmin xid", "compte_bancaire.xmin xid", "connexion_bancaire.xmin xid", "jeton.xmin xid", "utilisateur.xmin xid" },
            concurrencyColumns);
    }
}
