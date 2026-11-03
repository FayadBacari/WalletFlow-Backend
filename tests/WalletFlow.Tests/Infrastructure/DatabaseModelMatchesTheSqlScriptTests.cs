using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WalletFlow.Infrastructure.Persistence;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie que le modèle EF Core utilise exactement les tables et les colonnes du script SQL,
// sans en oublier et sans en inventer. Aucun serveur PostgreSQL n'est nécessaire.
public class DatabaseModelMatchesTheSqlScriptTests
{
    [Fact]
    public void EntityModelUsesExactlyTheTablesAndColumnsOfTheSqlScript()
    {
        var tablesAndColumnsDeclaredInTheSqlScript = ReadTablesAndColumnsDeclaredInTheSqlScript();
        var tablesAndColumnsMappedByEntityFrameworkCore = ReadTablesAndColumnsMappedByEntityFrameworkCore();

        Assert.Equal(tablesAndColumnsDeclaredInTheSqlScript, tablesAndColumnsMappedByEntityFrameworkCore);
    }

    // Lit les instructions CREATE TABLE du script SQL et renvoie « table.colonne » pour chaque colonne.
    private static List<string> ReadTablesAndColumnsDeclaredInTheSqlScript()
    {
        var pathOfTheSqlScriptCopiedNextToTheTests =
            Path.Combine(AppContext.BaseDirectory, "Database", "001_create_walletflow_schema.sql");
        var completeTextOfTheSqlCreationScript = File.ReadAllText(pathOfTheSqlScriptCopiedNextToTheTests);
        var tablesAndColumnsFoundInTheSqlScript = new List<string>();

        foreach (Match createTableStatementFoundInTheScript in
                 Regex.Matches(completeTextOfTheSqlCreationScript, @"CREATE TABLE (\w+) \((.*?)\n\);", RegexOptions.Singleline))
        {
            var nameOfTheTableCreatedByTheStatement = createTableStatementFoundInTheScript.Groups[1].Value;
            var bodyOfTheCreateTableStatementFound = createTableStatementFoundInTheScript.Groups[2].Value;

            foreach (Match columnDeclarationFoundInTheTableBody in
                     Regex.Matches(bodyOfTheCreateTableStatementFound,
                         @"^    ([a-z0-9_]+) +(UUID|VARCHAR|CHAR|INTEGER|SMALLINT|DATE|TIMESTAMPTZ|BOOLEAN|INET)",
                         RegexOptions.Multiline))
            {
                tablesAndColumnsFoundInTheSqlScript.Add(
                    nameOfTheTableCreatedByTheStatement + "." + columnDeclarationFoundInTheTableBody.Groups[1].Value);
            }
        }

        tablesAndColumnsFoundInTheSqlScript.Sort();
        return tablesAndColumnsFoundInTheSqlScript;
    }

    // Construit le modèle EF Core et renvoie « table.colonne » pour chaque propriété enregistrée.
    private static List<string> ReadTablesAndColumnsMappedByEntityFrameworkCore()
    {
        // Le modèle se construit sans se connecter : le serveur indiqué ici n'est jamais contacté.
        var optionsOfTheDatabaseContextWithoutRealServer = new DbContextOptionsBuilder<WalletFlowDatabaseContext>()
            .UseNpgsql("Host=localhost;Database=modele_sans_connexion")
            .Options;
        using var databaseContextUsedOnlyToReadTheModel = new WalletFlowDatabaseContext(optionsOfTheDatabaseContextWithoutRealServer);
        var tablesAndColumnsFoundInTheEntityModel = new List<string>();

        foreach (var entityTypeMappedByEntityFrameworkCore in databaseContextUsedOnlyToReadTheModel.Model.GetEntityTypes())
        {
            var nameOfTheTableMappedToThisEntityType = entityTypeMappedByEntityFrameworkCore.GetTableName()!;
            var tableIdentifierUsedToReadTheColumnNames = StoreObjectIdentifier.Table(nameOfTheTableMappedToThisEntityType, null);

            foreach (var propertyMappedToAColumnOfTheTable in entityTypeMappedByEntityFrameworkCore.GetProperties())
            {
                tablesAndColumnsFoundInTheEntityModel.Add(
                    nameOfTheTableMappedToThisEntityType + "." +
                    propertyMappedToAColumnOfTheTable.GetColumnName(tableIdentifierUsedToReadTheColumnNames));
            }
        }

        tablesAndColumnsFoundInTheEntityModel.Sort();
        return tablesAndColumnsFoundInTheEntityModel;
    }
}
