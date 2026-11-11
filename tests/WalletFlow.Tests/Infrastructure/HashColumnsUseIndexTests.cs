using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Infrastructure.Persistence;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Les empreintes de session et d'appareil sont des CHAR(64), comme celle des jetons (MPD §1.1).
// EF Core doit les chercher avec ce type : avec du texte, PostgreSQL convertirait la colonne et n'utiliserait plus son index.
// Chaque requête connectée cherche sa session : sans index, elle parcourrait toute la table.
public class HashColumnsUseIndexTests
{
    [Fact]
    public async Task SessionSearchUsesThePrimaryKeyOfItsTable()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        await ForbidFullTableScansAsync(databaseContext);

        var sessionHash = new string('a', 64);
        await using var command = databaseContext.UserSessions
            .Where(row => row.Id == sessionHash)
            .CreateDbCommand();

        Assert.Contains("session_pkey", await ReadQueryPlanAsync(command));
    }

    [Fact]
    public async Task DeviceSearchUsesTheUniqueIndexOfItsToken()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        await ForbidFullTableScansAsync(databaseContext);

        var deviceHash = new string('b', 64);
        await using var command = databaseContext.Devices
            .Where(row => row.DeviceToken == deviceHash)
            .CreateDbCommand();

        Assert.Contains("appareil_jeton_appareil_key", await ReadQueryPlanAsync(command));
    }

    // Sur une table presque vide, PostgreSQL préfère lire toute la table : les tests le lui interdisent.
    private static async Task ForbidFullTableScansAsync(WalletFlowDatabaseContext databaseContext)
    {
        await databaseContext.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");
    }

    // Demande à PostgreSQL comment il exécuterait la commande, et renvoie son plan en un seul texte.
    private static async Task<string> ReadQueryPlanAsync(DbCommand command)
    {
        command.CommandText = "EXPLAIN " + command.CommandText;

        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join("\n", lines);
    }
}
