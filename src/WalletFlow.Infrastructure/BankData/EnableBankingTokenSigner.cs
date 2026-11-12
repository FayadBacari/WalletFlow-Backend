using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WalletFlow.Infrastructure.BankData;

// Signe les jetons qui ouvrent l'API d'Enable Banking : un JWT RS256, signé par la clé privée de l'application WalletFlow.
// La clé reste sur le serveur, avec les autres secrets (SS §7). Un jeton sert une heure, puis un nouveau est signé.
public sealed class EnableBankingTokenSigner : IDisposable
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(5);
    public const int MinimumKeySize = 2048;

    private readonly RSA privateKey;
    private readonly string applicationId;
    private readonly TimeProvider clock;
    private readonly object tokenLock = new object();
    private string? currentToken = null;
    private DateTimeOffset currentTokenExpiresAt;

    // Reçoit l'identifiant de l'application chez Enable Banking, sa clé privée au format PEM, et l'horloge.
    // Une clé illisible ou trop courte fait refuser le démarrage de l'API.
    public EnableBankingTokenSigner(string applicationId, string privateKeyPem, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new InvalidOperationException("L'identifiant de l'application Enable Banking est vide.");
        }

        privateKey = RSA.Create();

        try
        {
            privateKey.ImportFromPem(privateKeyPem);
        }
        catch (ArgumentException exception)
        {
            privateKey.Dispose();
            throw new InvalidOperationException("La clé privée d'Enable Banking n'est pas une clé RSA lisible au format PEM.", exception);
        }

        if (privateKey.KeySize < MinimumKeySize)
        {
            privateKey.Dispose();
            throw new InvalidOperationException("La clé privée d'Enable Banking doit faire au moins 2048 bits.");
        }

        this.applicationId = applicationId;
        this.clock = clock;
    }

    // Donne un jeton encore valable au moins 5 minutes, ou en signe un nouveau.
    public string GetToken()
    {
        var now = clock.GetUtcNow();

        lock (tokenLock)
        {
            if (currentToken != null && now < currentTokenExpiresAt - RenewalMargin)
            {
                return currentToken;
            }

            var issuedAt = now.ToUnixTimeSeconds();
            var expiresAt = issuedAt + (long)TokenLifetime.TotalSeconds;
            var header = JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "RS256", kid = applicationId });
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { iss = "enablebanking.com", aud = "api.enablebanking.com", iat = issuedAt, exp = expiresAt });
            var signedPart = Base64Url.EncodeToString(header) + "." + Base64Url.EncodeToString(payload);
            var signature = privateKey.SignData(Encoding.ASCII.GetBytes(signedPart), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            currentToken = signedPart + "." + Base64Url.EncodeToString(signature);
            currentTokenExpiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAt);

            return currentToken;
        }
    }

    // Efface la clé de la mémoire quand l'API s'arrête (nom imposé par le framework).
    public void Dispose()
    {
        privateKey.Dispose();
    }
}
