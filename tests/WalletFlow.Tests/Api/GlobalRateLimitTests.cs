using System.Net;
using WalletFlow.Api.Configuration;
using static WalletFlow.Tests.Api.LoginRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la limite globale : 600 requêtes par minute et par adresse IP, toutes routes ensemble, avec les adresses IPv6
// comptées par bloc /64 (SS §6).
public class GlobalRateLimitTests
{
    private const string ProtectedRoute = "/api/categories";

    // Donne le compteur de cette adresse.
    private static string FindCounterKey(string address)
    {
        return RateLimitingConfiguration.FindCounterKey(IPAddress.Parse(address));
    }

    // Crée un client qui envoie un cookie de session inventé depuis cette adresse, comme un robot.
    private static HttpClient CreateRobot(ApiFactoryWithFakes api, string address)
    {
        var robot = CreateClientWithManualCookies(api, SessionCookiesConfiguration.SessionCookieName + "=jeton-de-session-invente");
        robot.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, address);

        return robot;
    }

    // Une adresse IPv4 reste elle-même, même vue en IPv6 : sinon, toutes les adresses IPv4 partageraient un seul compteur.
    // Une adresse IPv6 compte pour tout son bloc /64.
    [Fact]
    public void AddressesAreCountedByIpv4AddressOrByIpv6Block()
    {
        Assert.Equal("203.0.113.7", FindCounterKey("203.0.113.7"));
        Assert.Equal("203.0.113.7", FindCounterKey("::ffff:203.0.113.7"));
        Assert.NotEqual(FindCounterKey("::ffff:203.0.113.7"), FindCounterKey("::ffff:198.51.100.9"));
        Assert.Equal("2001:db8:1:2::/64", FindCounterKey("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));
        Assert.Equal(FindCounterKey("2001:db8:1:2::1"), FindCounterKey("2001:db8:1:2:ffff:ffff:ffff:ffff"));
        Assert.NotEqual(FindCounterKey("2001:db8:1:2::1"), FindCounterKey("2001:db8:1:3::1"));
        Assert.Equal("adresse-inconnue", RateLimitingConfiguration.FindCounterKey(null));
    }

    // La 601e requête de la minute d'une même adresse reçoit « trop de requêtes », sur n'importe quelle route : le cookie
    // inventé n'est même plus cherché en base. Une autre adresse passe encore.
    [Fact]
    public async Task SixHundredFirstRequestOfTheMinuteFromOneAddressIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var robot = CreateRobot(api, "203.0.113.30");
        var visitor = CreateRobot(api, "203.0.113.31");

        for (var requestNumber = 1; requestNumber <= 600; requestNumber++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await robot.GetAsync(ProtectedRoute)).StatusCode);
        }

        var refused = await robot.GetAsync(LoginEndpoint);

        await TooManyRequestsAssertions.AssertTooManyRequestsWithinAsync(refused, TimeSpan.FromMinutes(1));
        Assert.Equal(600, api.SessionRepository.SessionSearchCount);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(ProtectedRoute)).StatusCode);
    }

    // Un robot qui change d'adresse dans son bloc IPv6 /64 reste sur le même compteur. Le bloc voisin a le sien.
    [Fact]
    public async Task OtherAddressOfTheSameIpv6BlockSharesTheCounter()
    {
        using var api = new ApiFactoryWithFakes();
        var robot = CreateRobot(api, "2001:db8:1:2::1");

        for (var requestNumber = 1; requestNumber <= 600; requestNumber++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await robot.GetAsync(ProtectedRoute)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await CreateRobot(api, "2001:db8:1:2:abcd::9").GetAsync(ProtectedRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateRobot(api, "2001:db8:1:3::1").GetAsync(ProtectedRoute)).StatusCode);
    }

    // SS §6 : fige la limite écrite dans la stratégie. Le test ne peut pas attendre une minute pour la vérifier.
    [Fact]
    public void GlobalLimitFollowsTheValuesOfTheSecurityStrategy()
    {
        Assert.Equal(600, RateLimitingConfiguration.CreateGlobalLimit().PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), RateLimitingConfiguration.CreateGlobalLimit().Window);
    }
}
