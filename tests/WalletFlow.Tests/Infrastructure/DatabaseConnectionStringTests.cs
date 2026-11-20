using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Tests.Api;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie que l'API se connecte toujours à la base sans chiffrement GSS, et sans rien changer d'autre à sa chaîne de connexion.
public class DatabaseConnectionStringTests
{
    // Le chiffrement GSS est coupé, qu'il soit absent de la chaîne, préféré ou exigé.
    [Theory]
    [InlineData("Host=localhost;Database=walletflow;Username=walletflow_app")]
    [InlineData("Host=localhost;Database=walletflow;Username=walletflow_app;GSS Encryption Mode=Prefer")]
    [InlineData("Host=localhost;Database=walletflow;Username=walletflow_app;GSS Encryption Mode=Require")]
    public void GssEncryptionIsAlwaysDisabled(string connectionString)
    {
        var prepared = new NpgsqlConnectionStringBuilder(DatabaseConnectionString.WithoutGssEncryption(connectionString));

        Assert.Equal(GssEncryptionMode.Disable, prepared.GssEncryptionMode);
    }

    // Les autres réglages restent ceux de la chaîne : le serveur, le compte, son mot de passe, même avec un point-virgule, et le
    // chiffrement TLS.
    [Fact]
    public void OtherSettingsAreKept()
    {
        const string connectionString =
            "Host=serveur.exemple.test;Port=5433;Database=walletflow;Username=walletflow_app;Password='mot;de=passe';"
            + "SSL Mode=VerifyFull;Timeout=7;Maximum Pool Size=12";
        var original = new NpgsqlConnectionStringBuilder(connectionString);

        var prepared = new NpgsqlConnectionStringBuilder(DatabaseConnectionString.WithoutGssEncryption(connectionString));
        original.GssEncryptionMode = GssEncryptionMode.Disable;

        Assert.True(original.EquivalentTo(prepared));
        Assert.Equal("mot;de=passe", prepared.Password);
        Assert.Equal(SslMode.VerifyFull, prepared.SslMode);
    }

    // L'API branche la chaîne préparée sur EF Core comme sur le verrou de l'instance unique.
    [Fact]
    public void ApiConnectsWithoutGssEncryption()
    {
        using var api = new ApiFactoryWithFakes();
        using var scope = api.Services.CreateScope();
        var lockField = typeof(SingleInstanceLock).GetField("connectionString", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var databaseConnection = scope.ServiceProvider.GetRequiredService<WalletFlowDatabaseContext>().Database.GetConnectionString();
        var lockConnection = (string)lockField.GetValue(api.Services.GetRequiredService<SingleInstanceLock>())!;

        Assert.Equal(GssEncryptionMode.Disable, new NpgsqlConnectionStringBuilder(databaseConnection).GssEncryptionMode);
        Assert.Equal(GssEncryptionMode.Disable, new NpgsqlConnectionStringBuilder(lockConnection).GssEncryptionMode);
    }

    // Une vraie connexion à la base des tests s'ouvre avec la chaîne préparée.
    [Fact]
    public async Task PreparedConnectionStringOpensARealConnection()
    {
        await using var connection = new NpgsqlConnection(
            DatabaseConnectionString.WithoutGssEncryption(LocalTestDatabase.ReadConnectionString()));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT 1", connection);

        Assert.Equal(1, await command.ExecuteScalarAsync());
    }
}
