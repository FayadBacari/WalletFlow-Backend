using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie que les routes de l'API ne répondent qu'en HTTPS (SS §7). Sans port HTTPS connu, la redirection ne peut pas se faire :
// une requête en HTTP simple est alors refusée, au lieu de répondre 500 à une écriture ou en clair à une lecture. Les points de
// santé restent ouverts en HTTP, pour l'hébergeur.
public class HttpsOnlyTests
{
    // Crée un client qui parle à l'API en HTTP simple, sans suivre de redirection.
    private static HttpClient CreatePlainHttpClient(WebApplicationFactory<Program> api)
    {
        return api.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });
    }

    // Vérifie qu'une réponse est le refus d'une requête en HTTP simple : un 400 au format des requêtes refusées, avec une seule
    // erreur, sans clé de champ, qui dit de passer en HTTPS.
    private static async Task AssertHttpsRefusalAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ApiErrorConfiguration.RefusedRequestTitle, body.GetProperty("title").GetString());
        var errors = body.GetProperty("errors").EnumerateObject().ToList();
        Assert.Equal(ApiErrorConfiguration.GeneralErrorsKey, Assert.Single(errors).Name);
        Assert.Equal(ApiErrorConfiguration.HttpsRequiredMessage, Assert.Single(errors[0].Value.EnumerateArray()).GetString());
    }

    // Chaque route de l'API, publique ou non, en lecture comme en écriture, refuse une requête en HTTP simple.
    [Fact]
    public async Task EveryRouteRefusesPlainHttp()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreatePlainHttpClient(api);
        var routes = api.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
        var testedRouteCount = 0;

        foreach (var route in routes)
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

            await AssertHttpsRefusalAsync(response);
        }

        Assert.True(testedRouteCount >= 64, "L'inventaire n'a trouvé que " + testedRouteCount + " routes : il ne regarde plus les bonnes.");
    }

    // En production, une inscription en HTTP simple est refusée sans rien enregistrer ni poser de cookie. Le refus va au journal
    // comme un avertissement, jamais comme une panne, et la réponse garde les en-têtes de sécurité.
    [Fact]
    public async Task RegistrationOverPlainHttpSavesNothing()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = CreatePlainHttpClient(productionApi);

        var response = await client.PostAsJsonAsync(RegistrationEndpoint, new { email = "visiteur@exemple.test", password = ValidPassword });

        await AssertHttpsRefusalAsync(response);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
        Assert.Empty(productionApi.AccountRepository.SavedUsers);
        Assert.Empty(productionApi.SecurityEventRepository.SavedSecurityEvents);
        Assert.Contains(productionApi.LoggerProvider.Messages, message =>
            message.StartsWith("Warning : Une requête vers l'API est arrivée en HTTP simple", StringComparison.Ordinal));
        Assert.DoesNotContain(productionApi.LoggerProvider.Messages, message =>
            message.StartsWith("Error", StringComparison.Ordinal) || message.StartsWith("Critical", StringComparison.Ordinal));
    }

    // En production, la demande du jeton anti-CSRF en HTTP simple, qui répondait 500, reçoit le même refus, sans cookie.
    [Fact]
    public async Task CsrfTokenOverPlainHttpIsRefused()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = CreatePlainHttpClient(productionApi);

        var response = await client.GetAsync(CsrfTokenEndpoint);

        await AssertHttpsRefusalAsync(response);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(productionApi.LoggerProvider.Messages, message => message.StartsWith("Error", StringComparison.Ordinal));
    }

    // Les points de santé répondent en HTTP simple comme en HTTPS : l'hébergeur les sonde sans passer par le proxy.
    [Theory]
    [InlineData(HealthConfiguration.LivePath)]
    [InlineData(HealthConfiguration.ReadyPath)]
    [InlineData(HealthConfiguration.TasksPath)]
    public async Task HealthEndpointsAnswerOverPlainHttp(string path)
    {
        using var productionApi = new ApiFactoryInProduction();

        var overHttp = await CreatePlainHttpClient(productionApi).GetAsync(path);
        var overHttps = await productionApi.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, overHttp.StatusCode);
        Assert.Equal(overHttps.StatusCode, overHttp.StatusCode);
        Assert.Equal(await overHttps.Content.ReadAsStringAsync(), await overHttp.Content.ReadAsStringAsync());
    }

    // Une requête en HTTP du proxy de la même machine, qui dit avoir reçu du HTTPS, passe le contrôle : sans session, elle reçoit
    // 401. Le même en-tête envoyé par un visiteur ne la fait pas passer.
    [Fact]
    public async Task HttpsOfALocalProxyIsBelievedButNotThatOfAVisitor()
    {
        using var api = new ApiFactoryWithFakes();

        var fromProxy = await SendClaimingHttpsAsync(api, "127.0.0.1");
        var fromVisitor = await SendClaimingHttpsAsync(api, "198.51.100.9");

        Assert.Equal(HttpStatusCode.Unauthorized, fromProxy.StatusCode);
        await AssertHttpsRefusalAsync(fromVisitor);
    }

    // Envoie en HTTP simple, depuis cette adresse, une lecture qui dit venir de HTTPS.
    private static async Task<HttpResponseMessage> SendClaimingHttpsAsync(ApiFactoryWithFakes api, string socketAddress)
    {
        var client = CreatePlainHttpClient(api);
        client.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, socketAddress);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        return await client.GetAsync("/api/categories");
    }
}
