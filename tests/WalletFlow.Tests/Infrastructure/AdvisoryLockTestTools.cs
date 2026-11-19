using Npgsql;
using WalletFlow.Infrastructure.Persistence;

namespace WalletFlow.Tests.Infrastructure;

// Outils des tests du verrou de l'instance unique, sur la vraie base. Chaque test prend sa propre clé : les tests ne se gênent
// pas entre eux, ni une API qui tournerait sur la même base.
public static class AdvisoryLockTestTools
{
    // Tire une clé de verrou au hasard, jamais celle de l'API.
    public static long NewKey()
    {
        long key;

        do
        {
            key = Random.Shared.NextInt64();
        }
        while (key == SingleInstanceLock.ApiInstanceKey);

        return key;
    }

    // Crée un verrou sur la base des tests.
    public static SingleInstanceLock CreateLock(long key)
    {
        return new SingleInstanceLock(LocalTestDatabase.ReadConnectionString(), key);
    }

    // Coupe la session de la base qui tient ce verrou, comme une connexion perdue. Le verrou part avec elle.
    public static async Task TerminateHolderAsync(long key)
    {
        await using var lookup = CreateLock(key);
        var holder = await lookup.FindHolderAsync() ?? throw new InvalidOperationException("Personne ne tient ce verrou.");
        await using var connection = new NpgsqlConnection(LocalTestDatabase.ReadConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", connection);
        command.Parameters.AddWithValue("pid", holder);

        Assert.Equal(true, await command.ExecuteScalarAsync());
    }

    // Attend qu'une condition lue dans la base devienne vraie, 5 secondes au plus.
    public static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var checkNumber = 1; checkNumber <= 100; checkNumber++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.True(await condition(), "La condition attendue n'est jamais devenue vraie.");
    }
}
