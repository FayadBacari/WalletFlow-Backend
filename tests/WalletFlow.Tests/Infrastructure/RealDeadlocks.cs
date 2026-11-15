using Npgsql;

namespace WalletFlow.Tests.Infrastructure;

// Les outils des vrais tests d'interblocage : une autre transaction, sur sa propre connexion à la base, verrouille une ligne,
// attend que la requête testée soit bloquée par elle, puis demande une ligne que cette requête tient, ce qui ferme le cycle.
// L'autre transaction attend la moitié du délai de PostgreSQL avant de fermer le cycle : la requête testée, qui attend
// depuis plus longtemps, vérifie la première et c'est elle que PostgreSQL arrête, à coup sûr.
public static class RealDeadlocks
{
    public static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(30);

    // Verrouille une ligne dans l'autre transaction, avec cette requête qui reçoit son identifiant dans @id.
    public static async Task LockRowAsync(NpgsqlTransaction otherTransaction, string lockQuery, Guid rowId)
    {
        await using var command = new NpgsqlCommand(lockQuery, otherTransaction.Connection, otherTransaction);
        command.Parameters.AddWithValue("id", rowId);
        await command.ExecuteScalarAsync();
    }

    // Lit le délai d'attente après lequel PostgreSQL vérifie les interblocages (deadlock_timeout, une seconde par défaut).
    public static async Task<TimeSpan> ReadDeadlockDetectionDelayAsync(NpgsqlTransaction otherTransaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setting::int FROM pg_settings WHERE name = 'deadlock_timeout' AND unit = 'ms'",
            otherTransaction.Connection,
            otherTransaction);
        var milliseconds = (int)(await command.ExecuteScalarAsync())!;

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    // Attend que PostgreSQL montre la requête testée bloquée par l'autre transaction.
    public static async Task WaitUntilBlockedAsync(NpgsqlTransaction otherTransaction, int blockedProcessId)
    {
        var deadline = DateTime.UtcNow + MaximumWait;

        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("SELECT @other = ANY(pg_blocking_pids(@blocked))", otherTransaction.Connection, otherTransaction);
            command.Parameters.AddWithValue("other", otherTransaction.Connection!.ProcessID);
            command.Parameters.AddWithValue("blocked", blockedProcessId);

            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("La requête testée n'a jamais attendu l'autre transaction.");
    }
}
