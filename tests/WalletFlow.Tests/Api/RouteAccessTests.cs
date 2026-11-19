using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace WalletFlow.Tests.Api;

// Vérifie qui peut appeler chaque route (SS §4) : toute route demande une session, sauf les routes publiques prévues. Chaque
// contrôleur le dit lui-même, avec [Authorize] ou [AllowAnonymous], en plus de la règle commune de Program.cs : CodeQL ne voit
// que ces attributs.
public class RouteAccessTests
{
    // Les seules routes appelées sans session : l'inscription, la connexion et les liens reçus par e-mail, que l'on ouvre sans
    // être connecté, et la demande de support d'un visiteur.
    private static readonly List<string> PublicRoutes = new List<string>
    {
        "POST api/account/register",
        "POST api/account/confirm-email",
        "POST api/account/login",
        "POST api/account/login/two-factor",
        "POST api/account/forgot-password",
        "POST api/account/reset-password",
        "POST api/account/unlock",
        "POST api/account/disable-two-factor",
        "POST api/account/confirm-email-change",
        "POST api/support-requests/visitor"
    };

    // Donne toutes les routes des contrôleurs.
    private static List<ActionDescriptor> ReadRoutes(ApiFactoryWithFakes api)
    {
        return api.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.ToList();
    }

    // Écrit une route comme « POST api/account/login ».
    private static string Describe(ActionDescriptor route)
    {
        var method = route.EndpointMetadata.OfType<IHttpMethodMetadata>().SelectMany(metadata => metadata.HttpMethods).Single();

        return method + " " + route.AttributeRouteInfo!.Template;
    }

    // Dit si une route est publique.
    private static bool IsPublic(ActionDescriptor route)
    {
        return route.EndpointMetadata.OfType<IAllowAnonymous>().Any();
    }

    // Chaque route dit elle-même qui peut l'appeler, par un attribut de son contrôleur ou de son action : une nouvelle route ne
    // peut pas s'en remettre à la seule règle commune, que CodeQL ne voit pas.
    [Fact]
    public void EveryRouteSaysWhoMayCallIt()
    {
        using var api = new ApiFactoryWithFakes();

        var routesWithoutAttribute = ReadRoutes(api)
            .Where(route => !IsPublic(route) && !route.EndpointMetadata.OfType<IAuthorizeData>().Any())
            .Select(Describe)
            .ToList();

        Assert.Empty(routesWithoutAttribute);
    }

    // Seules les routes prévues sont publiques : un [AllowAnonymous] ajouté par erreur fait échouer ce test.
    [Fact]
    public void OnlyThePlannedRoutesArePublic()
    {
        using var api = new ApiFactoryWithFakes();

        var publicRoutes = ReadRoutes(api).Where(IsPublic).Select(Describe).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(PublicRoutes.Order(StringComparer.Ordinal), publicRoutes);
    }

    // Sans session, toute autre route répond 401, avant de lire quoi que ce soit.
    [Fact]
    public async Task EveryOtherRouteNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        var routesAnsweredOtherwise = new List<string>();
        var testedRouteCount = 0;

        foreach (var route in ReadRoutes(api).Where(route => !IsPublic(route)))
        {
            var method = route.EndpointMetadata.OfType<IHttpMethodMetadata>().SelectMany(metadata => metadata.HttpMethods).Single();
            var template = route.AttributeRouteInfo!.Template!;
            var address = "/" + Regex.Replace(Regex.Replace(template, "\\{[^}]*:guid\\}", Guid.NewGuid().ToString()), "\\{[^}]*\\}", "valeur");
            using var request = new HttpRequestMessage(new HttpMethod(method), address);
            if (method != HttpMethod.Get.Method)
            {
                request.Content = JsonContent.Create(new { });
            }

            var response = await client.SendAsync(request);
            testedRouteCount = testedRouteCount + 1;

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                routesAnsweredOtherwise.Add(method + " " + template + " : " + (int)response.StatusCode);
            }
        }

        Assert.Empty(routesAnsweredOtherwise);
        Assert.True(testedRouteCount >= 54, "L'inventaire n'a trouvé que " + testedRouteCount + " routes : il ne regarde plus les bonnes.");
    }
}
