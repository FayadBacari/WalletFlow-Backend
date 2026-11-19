using System.Net;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la limite des routes qui appellent le prestataire bancaire : 20 par heure et par session, toutes ensemble (SS §6).
public class BankRateLimitTests
{
    private const string OtherUserEmail = "autre-utilisateur@exemple.test";

    // Les routes qui appellent le prestataire, avec leur méthode HTTP.
    private static readonly List<string> BankProviderRoutes = new List<string>
    {
        "GET api/banks", "POST api/bank-connections", "POST api/bank-connections/{connectionId:guid}/renew",
        "POST api/bank-connections/finish", "DELETE api/bank-connections/{connectionId:guid}"
    };

    // Ajoute un compte, le connecte dans un nouveau navigateur, et renvoie ce navigateur.
    private static async Task<HttpClient> LogInAsync(ApiFactoryWithFakes api, string email = Email)
    {
        await AddAccountAsync(api, email);
        var client = CreateBrowserClient(api);
        await LogInThenGetCsrfTokenAsync(client, email);

        return client;
    }

    // Le 21e appel de l'heure reçoit « trop de requêtes », et seulement pour cette session : une autre session appelle encore,
    // et la même session lit encore ses connexions, qui n'appellent pas le prestataire.
    [Fact]
    public async Task TwentyFirstBankCallOfTheHourIsRefusedForThisSessionOnly()
    {
        using var api = new ApiFactoryWithFakes();
        var client = await LogInAsync(api);
        var otherClient = await LogInAsync(api, OtherUserEmail);

        for (var callNumber = 1; callNumber <= 20; callNumber++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/banks")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/banks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherClient.GetAsync("/api/banks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/bank-connections")).StatusCode);
    }

    // Chaque route qui appelle le prestataire déclare cette limite : une nouvelle route ne peut pas l'oublier sans que ce test le voie.
    [Fact]
    public void EveryBankProviderRouteDeclaresTheLimit()
    {
        using var api = new ApiFactoryWithFakes();
        var routeProvider = api.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var limitedRoutes = new List<string>();

        foreach (var route in routeProvider.ActionDescriptors.Items)
        {
            var policy = route.EndpointMetadata.OfType<EnableRateLimitingAttribute>().LastOrDefault()?.PolicyName;

            if (policy == RateLimitingConfiguration.BankPolicy)
            {
                var method = route.ActionConstraints!.OfType<HttpMethodActionConstraint>().Single().HttpMethods.Single();
                limitedRoutes.Add(method + " " + route.AttributeRouteInfo!.Template!);
            }
        }

        limitedRoutes.Sort(StringComparer.Ordinal);
        var expectedRoutes = new List<string>(BankProviderRoutes);
        expectedRoutes.Sort(StringComparer.Ordinal);
        Assert.Equal(expectedRoutes, limitedRoutes);
        Assert.Equal(20, RateLimitingConfiguration.CreateBankLimit().PermitLimit);
        Assert.Equal(TimeSpan.FromHours(1), RateLimitingConfiguration.CreateBankLimit().Window);
    }
}
