using Npgsql;

namespace WalletFlow.Infrastructure.Persistence;

// Un verrou consultatif de PostgreSQL, tenu par une connexion à part tant que l'instance de l'API tourne : sur une même base,
// une seule connexion le tient à la fois. Cette connexion n'entre pas dans le pool de Npgsql : la fermer rend le verrou aussitôt,
// au lieu de le laisser tenu par une connexion en réserve. PostgreSQL le rend aussi de lui-même si la connexion se perd.
public sealed class SingleInstanceLock : IAsyncDisposable
{
    // La clé du verrou de l'API : « WalletFl » écrit en octets ASCII. Les tests prennent d'autres clés, pour ne jamais gêner une API
    // qui tourne sur la même base.
    public const long ApiInstanceKey = 0x57616C6C6574466C;

    private readonly string connectionString;
    private readonly long key;
    private NpgsqlConnection? connection = null;

    // Reçoit la base et la clé du verrou. La connexion garde la session en vie quand rien ne passe pendant longtemps.
    public SingleInstanceLock(string connectionString, long key)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            KeepAlive = 30
        };

        this.connectionString = builder.ConnectionString;
        this.key = key;
    }

    // Le processus de la base qui tient, ou tenait avant sa perte, le verrou de cette instance.
    public int? LastProcessId { get; private set; } = null;

    // Prend le verrou s'il est libre. Renvoie faux si une autre connexion le tient. Une base qui ne répond pas lève son erreur.
    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        await ReleaseAsync();
        var candidate = new NpgsqlConnection(connectionString);

        try
        {
            await candidate.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", candidate);
            command.Parameters.AddWithValue("key", key);

            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                connection = candidate;
                LastProcessId = candidate.ProcessID;
                return true;
            }
        }
        catch
        {
            await candidate.DisposeAsync();
            throw;
        }

        await candidate.DisposeAsync();
        return false;
    }

    // Vrai si le verrou est encore tenu, c'est-à-dire si sa connexion répond : une connexion perdue a perdu le verrou avec elle.
    public async Task<bool> IsHeldAsync(CancellationToken cancellationToken = default)
    {
        if (connection == null)
        {
            return false;
        }

        try
        {
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            await ReleaseAsync();
            return false;
        }
    }

    // Donne le processus de la base qui tient le verrou en ce moment, quelle que soit l'instance, ou null s'il est libre.
    public async Task<int?> FindHolderAsync(CancellationToken cancellationToken = default)
    {
        await using var lookup = new NpgsqlConnection(connectionString);
        await lookup.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT pid FROM pg_locks WHERE locktype = 'advisory' AND granted AND objsubid = 1 "
                + "AND classid = (@high)::oid AND objid = (@low)::oid",
            lookup);
        command.Parameters.AddWithValue("high", (key >> 32) & 0xFFFFFFFF);
        command.Parameters.AddWithValue("low", key & 0xFFFFFFFF);

        return await command.ExecuteScalarAsync(cancellationToken) as int?;
    }

    // Rend le verrou en fermant sa connexion.
    public async Task ReleaseAsync()
    {
        var heldConnection = connection;
        connection = null;

        if (heldConnection != null)
        {
            await heldConnection.DisposeAsync();
        }
    }

    // Rend le verrou quand l'API s'arrête (nom imposé par .NET).
    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync();
    }
}
