using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WalletFlow.Api.Configuration;
using static WalletFlow.Tests.Api.LoginRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la lecture de l'adresse du client derrière un proxy inverse : l'en-tête n'est cru que s'il vient d'un proxy
// déclaré (SS §6). Sinon, n'importe qui choisirait l'adresse sous laquelle il est compté par les limites et noté au journal.
public class ReverseProxyTests
{
    // Crée une API qui fait confiance au proxy 10.0.0.5 et au réseau 192.168.10.0/24.
    private static WebApplicationFactory<Program> CreateApiBehindProxy(ApiFactoryWithFakes api)
    {
        return api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(ReverseProxyConfiguration.KnownProxiesSetting + ":0", "10.0.0.5");
            builder.UseSetting(ReverseProxyConfiguration.KnownNetworksSetting + ":0", "192.168.10.0/24");
            builder.UseSetting("https_port", "443");
        });
    }

    // Envoie une connexion ratée depuis cette adresse, avec cet en-tête X-Forwarded-For, et renvoie l'adresse notée au journal.
    private static async Task<IPAddress?> ReadJournalAddressAsync(
        ApiFactoryWithFakes api,
        WebApplicationFactory<Program> server,
        string socketAddress,
        string forwardedFor)
    {
        var client = server.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, socketAddress);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);

        await SendLoginRequestAsync(client, Email, WrongPassword);

        return api.SecurityEventRepository.SavedSecurityEvents.Last().IpAddress;
    }

    [Fact]
    public async Task ClientAddressIsReadFromADeclaredProxyOnly()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        using var server = CreateApiBehindProxy(api);

        Assert.Equal(IPAddress.Parse("203.0.113.50"), await ReadJournalAddressAsync(api, server, "10.0.0.5", "203.0.113.50"));
        Assert.Equal(IPAddress.Parse("203.0.113.51"), await ReadJournalAddressAsync(api, server, "192.168.10.77", "203.0.113.51"));
        Assert.Equal(IPAddress.Parse("198.51.100.9"), await ReadJournalAddressAsync(api, server, "198.51.100.9", "203.0.113.52"));
    }

    // Sans proxy déclaré, seul un proxy de la même machine est cru.
    [Fact]
    public async Task WithoutDeclaredProxyOnlyALocalProxyIsBelieved()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);

        Assert.Equal(IPAddress.Parse("198.51.100.9"), await ReadJournalAddressAsync(api, api, "198.51.100.9", "203.0.113.52"));
        Assert.Equal(IPAddress.Parse("203.0.113.53"), await ReadJournalAddressAsync(api, api, "127.0.0.1", "203.0.113.53"));
    }

    // Le proxy qui reçoit le HTTPS parle à l'API en HTTP : son en-tête X-Forwarded-Proto évite la redirection. Le même en-tête
    // venu d'un visiteur ne change rien : sa requête en HTTP est redirigée vers HTTPS.
    [Fact]
    public async Task HttpsOfTheProxyIsBelievedOnlyFromADeclaredProxy()
    {
        using var api = new ApiFactoryWithFakes();
        using var server = CreateApiBehindProxy(api);

        var fromProxy = await SendOverHttpAsync(server, "10.0.0.5");
        var fromVisitor = await SendOverHttpAsync(server, "198.51.100.9");

        Assert.Equal(HttpStatusCode.Unauthorized, fromProxy.StatusCode);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, fromVisitor.StatusCode);
    }

    // Une adresse ou un réseau illisible empêche l'API de démarrer : un proxy mal déclaré compterait tous les visiteurs sous
    // une seule adresse.
    [Theory]
    [InlineData(ReverseProxyConfiguration.KnownProxiesSetting + ":0", "proxy-du-reseau")]
    [InlineData(ReverseProxyConfiguration.KnownNetworksSetting + ":0", "10.0.0.0/33")]
    [InlineData(ReverseProxyConfiguration.KnownNetworksSetting + ":0", "10.0.0.0")]
    public void UnreadableProxyIsRefusedAtStartup(string settingName, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [settingName] = value })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => ReverseProxyConfiguration.Read(configuration));

        Assert.Contains(value, exception.Message);
    }

    // Envoie en HTTP, depuis cette adresse, une requête qui dit venir de HTTPS.
    private static async Task<HttpResponseMessage> SendOverHttpAsync(WebApplicationFactory<Program> server, string socketAddress)
    {
        var client = server.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://walletflow.test")
        });
        client.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, socketAddress);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        return await client.GetAsync("/api/categories");
    }
}
