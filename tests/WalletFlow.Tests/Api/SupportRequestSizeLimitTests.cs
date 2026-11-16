using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.NotificationsAndSupport;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la limite de taille des routes du support avec le vrai serveur Kestrel, en HTTPS et en mode production : le serveur
// de test n'applique pas cette limite, et la route de l'utilisateur demande une session, dont les cookies Secure ne voyagent
// qu'en HTTPS. Le certificat est créé pour le test, et le client n'accepte que lui.
public class SupportRequestSizeLimitTests : IDisposable
{
    private const string RequestsEndpoint = "/api/support-requests";
    private const string VisitorEndpoint = RequestsEndpoint + "/visitor";

    // Un emoji tel qu'un navigateur peut l'écrire dans le JSON : deux \uXXXX, 12 octets. Le caractère « \ » est créé par
    // son code, pour qu'aucun outil ne lise la suite comme un caractère.
    private static readonly string Backslash = ((char)92).ToString();
    private static readonly string EscapedEmoji = Backslash + "ud83d" + Backslash + "ude00";

    // La plus longue adresse permise : 254 caractères.
    private static readonly string LongestEmailAddress =
        new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 58) + ".fr";

    private readonly X509Certificate2 certificate = ThrowawayHttpsCertificate.Create();
    private readonly ApiFactoryInProduction api = new ApiFactoryInProduction();

    // Démarre la vraie API sur un port libre, en HTTPS avec le certificat du test.
    public SupportRequestSizeLimitTests()
    {
        api.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.UseHttps(certificate)));
        api.StartServer();
    }

    // Arrête l'API et oublie le certificat (nom imposé par le framework).
    public void Dispose()
    {
        api.Dispose();
        certificate.Dispose();
    }

    // Crée un navigateur qui parle HTTPS à la vraie API, garde les cookies, et n'accepte que le certificat du test.
    private HttpClient CreateBrowser()
    {
        var address = api.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            ServerCertificateCustomValidationCallback = (message, serverCertificate, chain, errors) =>
                serverCertificate?.Thumbprint == certificate.Thumbprint
        };

        return new HttpClient(handler) { BaseAddress = new Uri(address) };
    }

    // Envoie ce texte JSON tel quel en POST, avec le jeton anti-CSRF s'il y en a un.
    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient browser, string address, string json, string? csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (csrfToken != null)
        {
            request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);
        }

        return await browser.SendAsync(request);
    }

    // Le plus long message permis, chaque caractère écrit comme un emoji en \uXXXX\uXXXX, passe sur les deux routes : celle
    // de l'utilisateur, et celle du visiteur avec la plus longue adresse permise.
    [Fact]
    public async Task BiggestValidRequestsPassOverRealHttps()
    {
        var browser = CreateBrowser();
        await AddAccountAsync(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browser);
        var longestMessage = string.Concat(Enumerable.Repeat(EscapedEmoji, SupportRules.MaximumTextLength));

        var fromUser = await PostJsonAsync(browser, RequestsEndpoint, "{\"message\":\"" + longestMessage + "\"}", csrfToken);
        var fromVisitor = await PostJsonAsync(
            CreateBrowser(),
            VisitorEndpoint,
            "{\"contactEmail\":\"" + LongestEmailAddress + "\",\"message\":\"" + longestMessage + "\"}",
            null);

        Assert.Equal(HttpStatusCode.Created, fromUser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fromVisitor.StatusCode);
        Assert.Equal(2, api.SupportRepository.SavedRequests.Count);
        Assert.All(api.SupportRepository.SavedRequests, request => Assert.Equal(5000, request.Message.EnumerateRunes().Count()));
    }

    // Une requête trop grosse reçoit 413, sans détail technique, et rien n'est enregistré.
    [Fact]
    public async Task RequestsBiggerThanTheLimitAreRefusedWithPayloadTooLarge()
    {
        var browser = CreateBrowser();
        await AddAccountAsync(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browser);
        var tooLongMessage = new string('a', (int)RequestSizeConfiguration.MaximumSupportRequestBytes);

        var fromUser = await PostJsonAsync(browser, RequestsEndpoint, "{\"message\":\"" + tooLongMessage + "\"}", csrfToken);
        var fromVisitor = await PostJsonAsync(
            CreateBrowser(),
            VisitorEndpoint,
            "{\"contactEmail\":\"visiteur@exemple.test\",\"message\":\"" + tooLongMessage + "\"}",
            null);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, fromUser.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, fromVisitor.StatusCode);
        var responseText = await fromVisitor.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Request body too large", responseText);
        Assert.DoesNotContain("65536", responseText);
        Assert.Empty(api.SupportRepository.SavedRequests);
    }
}
