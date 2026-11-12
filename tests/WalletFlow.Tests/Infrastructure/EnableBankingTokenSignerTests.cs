using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WalletFlow.Infrastructure.BankData;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les jetons signés qui ouvrent l'API d'Enable Banking : un JWT RS256 que sa clé publique vérifie (SS §7).
public class EnableBankingTokenSignerTests
{
    // Lit une partie JSON d'un jeton.
    private static JsonElement ReadPart(string encodedPart)
    {
        return JsonDocument.Parse(Base64Url.DecodeFromChars(encodedPart)).RootElement;
    }

    [Fact]
    public void TokenIsAnRs256JwtThatThePublicKeyVerifies()
    {
        using var key = RSA.Create(2048);
        var clock = new FakeTimeProvider();
        using var signer = new EnableBankingTokenSigner("application-de-test", key.ExportPkcs8PrivateKeyPem(), clock);

        var parts = signer.GetToken().Split('.');

        Assert.Equal(3, parts.Length);
        var header = ReadPart(parts[0]);
        var payload = ReadPart(parts[1]);
        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.GetProperty("typ").GetString());
        Assert.Equal("application-de-test", header.GetProperty("kid").GetString());
        Assert.Equal("enablebanking.com", payload.GetProperty("iss").GetString());
        Assert.Equal("api.enablebanking.com", payload.GetProperty("aud").GetString());
        Assert.Equal(clock.GetUtcNow().ToUnixTimeSeconds(), payload.GetProperty("iat").GetInt64());
        Assert.Equal(clock.GetUtcNow().ToUnixTimeSeconds() + 3600, payload.GetProperty("exp").GetInt64());
        Assert.True(key.VerifyData(
            Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
            Base64Url.DecodeFromChars(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
    }

    // Un jeton sert tant qu'il lui reste plus de 5 minutes, puis un nouveau est signé.
    [Fact]
    public void TokenIsReusedUntilFiveMinutesBeforeItsEnd()
    {
        using var key = RSA.Create(2048);
        var clock = new FakeTimeProvider();
        using var signer = new EnableBankingTokenSigner("application-de-test", key.ExportPkcs8PrivateKeyPem(), clock);
        var firstToken = signer.GetToken();

        clock.Advance(TimeSpan.FromMinutes(54));
        Assert.Equal(firstToken, signer.GetToken());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.NotEqual(firstToken, signer.GetToken());
    }

    // Une clé illisible ou trop courte fait refuser le démarrage de l'API.
    [Fact]
    public void UnreadableOrShortKeyIsRefused()
    {
        using var shortKey = RSA.Create(1024);

        Assert.Throws<InvalidOperationException>(() => new EnableBankingTokenSigner("application-de-test", "pas une clé", new FakeTimeProvider()));
        Assert.Throws<InvalidOperationException>(() => new EnableBankingTokenSigner("application-de-test", shortKey.ExportPkcs8PrivateKeyPem(), new FakeTimeProvider()));
    }

    [Fact]
    public void EmptyApplicationIdIsRefused()
    {
        using var key = RSA.Create(2048);

        Assert.Throws<InvalidOperationException>(() => new EnableBankingTokenSigner(" ", key.ExportPkcs8PrivateKeyPem(), new FakeTimeProvider()));
    }
}
