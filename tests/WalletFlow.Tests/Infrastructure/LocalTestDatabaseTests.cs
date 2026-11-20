using Npgsql;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie que les tests ne se connectent qu'à une base des tests : jamais à la base de développement « walletflow », ni à une
// autre base désignée par erreur.
public class LocalTestDatabaseTests
{
    private const string Password = "mot-de-passe-du-test";

    // Sans variable d'environnement, les tests utilisent la base locale des tests.
    [Fact]
    public void DefaultDatabaseIsTheLocalTestDatabase()
    {
        var connectionString = LocalTestDatabase.RefuseIfNotTestDatabase(LocalTestDatabase.DefaultConnectionString);

        Assert.Equal(LocalTestDatabase.DefaultConnectionString, connectionString);
        Assert.Equal("walletflow_tests", new NpgsqlConnectionStringBuilder(connectionString).Database);
    }

    // La base que les tests utilisent pour de vrai, celle de la variable ou la base locale, est une base des tests.
    [Fact]
    public void DatabaseInUseIsATestDatabase()
    {
        var databaseName = new NpgsqlConnectionStringBuilder(LocalTestDatabase.ReadConnectionString()).Database;

        Assert.EndsWith(LocalTestDatabase.NameEnding, databaseName, StringComparison.Ordinal);
    }

    // Une base des tests est acceptée telle quelle, sur une autre machine aussi.
    [Theory]
    [InlineData("Host=localhost;Database=walletflow_tests;Username=walletflow_app")]
    [InlineData("Host=serveur.exemple.test;Port=5433;Database=copie_tests;Username=walletflow_app;Password=" + Password)]
    public void TestDatabaseIsAccepted(string connectionString)
    {
        Assert.Equal(connectionString, LocalTestDatabase.RefuseIfNotTestDatabase(connectionString));
    }

    // Toute autre base est refusée, même écrite avec un synonyme, en double ou sans nom (Npgsql prendrait alors le nom du
    // compte). Le refus donne le nom de la base que Npgsql aurait utilisée, et jamais le mot de passe.
    [Theory]
    [InlineData("Host=localhost;Database=walletflow;Username=walletflow_app;Password=" + Password, "walletflow")]
    [InlineData("Host=localhost;DB=walletflow;Username=walletflow_app;Password=" + Password, "walletflow")]
    [InlineData("Host=localhost;Database=walletflow_tests;Database=walletflow;Password=" + Password, "walletflow")]
    [InlineData("Host=localhost;Database=walletflow_TESTS;Password=" + Password, "walletflow_TESTS")]
    [InlineData("Host=localhost;Database=walletflow_tests_ancienne;Password=" + Password, "walletflow_tests_ancienne")]
    [InlineData("Host=localhost;Database=tests;Password=" + Password, "tests")]
    [InlineData("Host=localhost;Username=walletflow_app;Password=" + Password, "")]
    public void OtherDatabaseIsRefused(string connectionString, string databaseName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => LocalTestDatabase.RefuseIfNotTestDatabase(connectionString));

        Assert.Equal(databaseName, new NpgsqlConnectionStringBuilder(connectionString).Database ?? "");
        Assert.Contains("« " + databaseName + " »", exception.Message);
        Assert.DoesNotContain(Password, exception.Message);
    }
}
