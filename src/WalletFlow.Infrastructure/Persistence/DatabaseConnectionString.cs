using Npgsql;

namespace WalletFlow.Infrastructure.Persistence;

// Prépare la chaîne de connexion à la base, une fois au démarrage, avant tout accès de l'API.
public static class DatabaseConnectionString
{
    // Coupe le chiffrement GSS de la connexion, même si la chaîne en demande un : il passe par Kerberos, que WalletFlow n'utilise
    // pas. Une connexion chiffrée passe par TLS, avec « SSL Mode ». Depuis Npgsql 10, une connexion essaie d'abord GSS ; sur un
    // Linux sans Kerberos, comme l'image Docker de l'API, Npgsql écrit alors « Cannot load library libgssapi_krb5.so.2 » sur la
    // sortie d'erreur, puis se connecte sans GSS. Ce faux message d'erreur cacherait les vrais. Les autres réglages ne changent pas.
    public static string WithoutGssEncryption(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable
        };

        return builder.ConnectionString;
    }
}
