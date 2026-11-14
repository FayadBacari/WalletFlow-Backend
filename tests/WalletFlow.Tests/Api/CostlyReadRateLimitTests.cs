using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.AccountsAndCategories;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la limite des lectures coûteuses : 60 par minute et par session, toutes ces routes ensemble (SS §6).
public class CostlyReadRateLimitTests
{
    private const string OtherUserEmail = "autre-utilisateur@exemple.test";

    // Les routes qui additionnent beaucoup de lignes à chaque appel.
    private static readonly List<string> CostlyRoutes = new List<string>
    {
        "api/transactions", "api/charts/categories", "api/charts/months", "api/charts/comparison", "api/bags", "api/bags/{bagId:guid}/movements"
    };

    // Ajoute un compte avec ses catégories, le connecte dans un nouveau navigateur, et renvoie ce navigateur.
    private static async Task<HttpClient> LogInAsync(ApiFactoryWithFakes api, string email = Email)
    {
        var account = await AddAccountAsync(api, email);
        api.CategoryRepository.SavedCategories.AddRange(DefaultCategories.CreateForUser(account.Id));
        var client = CreateBrowserClient(api);
        await LogInThenGetCsrfTokenAsync(client, email);

        return client;
    }

    // La 61e lecture coûteuse de la minute reçoit « trop de requêtes », quelle que soit la route, et seulement pour cette session :
    // une autre session lit encore, et les autres routes de la même session aussi.
    [Fact]
    public async Task SixtyFirstCostlyReadOfTheMinuteIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var client = await LogInAsync(api);
        var otherClient = await LogInAsync(api, OtherUserEmail);
        var addresses = new List<string> { "/api/bags", "/api/charts/comparison", "/api/transactions", "/api/charts/categories?from=2026-11-01&to=2026-11-30" };

        for (var readNumber = 0; readNumber < 60; readNumber++)
        {
            var response = await client.GetAsync(addresses[readNumber % addresses.Count]);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var refused = await client.GetAsync("/api/charts/months?from=2026-11-01&to=2026-11-30");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("Trop de demandes. Réessaie plus tard.", JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.OK, (await otherClient.GetAsync("/api/bags")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories")).StatusCode);
    }

    // Chaque lecture coûteuse déclare cette limite : une nouvelle route coûteuse ne peut pas l'oublier sans que ce test le voie.
    [Fact]
    public void EveryCostlyReadDeclaresTheLimit()
    {
        using var api = new ApiFactoryWithFakes();
        var routeProvider = api.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var limitedRoutes = new List<string>();

        foreach (var route in routeProvider.ActionDescriptors.Items)
        {
            var policy = route.EndpointMetadata.OfType<EnableRateLimitingAttribute>().LastOrDefault()?.PolicyName;

            if (policy == RateLimitingConfiguration.CostlyReadPolicy)
            {
                limitedRoutes.Add(route.AttributeRouteInfo!.Template!);
            }
        }

        limitedRoutes.Sort(StringComparer.Ordinal);
        var expectedRoutes = new List<string>(CostlyRoutes);
        expectedRoutes.Sort(StringComparer.Ordinal);
        Assert.Equal(expectedRoutes, limitedRoutes);
        Assert.Equal(60, RateLimitingConfiguration.CreateCostlyReadLimit().PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), RateLimitingConfiguration.CreateCostlyReadLimit().Window);
    }
}
