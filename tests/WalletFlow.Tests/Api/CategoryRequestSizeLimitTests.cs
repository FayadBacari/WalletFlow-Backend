using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.AccountsAndCategories;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la limite de taille des routes des catégories avec le vrai serveur Kestrel, en HTTPS et en mode production.
// Le serveur de test n'applique pas cette limite, et ces routes demandent une session, dont les cookies Secure
// ne voyagent qu'en HTTPS. Le certificat est créé pour le test, et le client n'accepte que lui.
public class CategoryRequestSizeLimitTests : IDisposable
{
    private readonly X509Certificate2 certificate = CreateThrowawayCertificate();
    private readonly ApiFactoryInProduction api = new ApiFactoryInProduction();

    // Démarre la vraie API sur un port libre, en HTTPS avec le certificat du test.
    public CategoryRequestSizeLimitTests()
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

    // Crée un certificat jetable pour 127.0.0.1, valable une heure, avec sa clé privée.
    private static X509Certificate2 CreateThrowawayCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(alternativeNames.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

        using var selfSigned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        // macOS ne sert en HTTPS qu'un certificat dont la clé est rangée avec lui : un passage par PKCS#12 l'y range.
        return X509CertificateLoader.LoadPkcs12(selfSigned.Export(X509ContentType.Pkcs12), null);
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

    // Ajoute le compte des tests avec ses catégories par défaut, le connecte, et renvoie son jeton anti-CSRF.
    private async Task<string> LogInWithCategoriesAsync(HttpClient browser)
    {
        var account = await AddAccountAsync(api);
        api.CategoryRepository.SavedCategories.AddRange(DefaultCategories.CreateForUser(account.Id));

        return await LogInThenGetCsrfTokenAsync(browser);
    }

    // Envoie ce texte JSON tel quel, avec le jeton anti-CSRF.
    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient browser, HttpMethod method, string address, string json, string csrfToken)
    {
        var request = new HttpRequestMessage(method, address)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);

        return await browser.SendAsync(request);
    }

    // Une requête trop grosse reçoit 413, sans détail technique, et ne change rien : la création, le nom et le budget.
    [Fact]
    public async Task CategoryRequestsBiggerThanTheLimitAreRefusedWithPayloadTooLarge()
    {
        var browser = CreateBrowser();
        var csrfToken = await LogInWithCategoriesAsync(browser);
        var leisure = api.CategoryRepository.SavedCategories.Single(category => category.Name == "Loisirs");
        var padding = new string('a', 2000);

        var creation = await SendJsonAsync(browser, HttpMethod.Post, "/api/categories", "{\"name\":\"" + padding + "\"}", csrfToken);
        var rename = await SendJsonAsync(browser, HttpMethod.Put, "/api/categories/" + leisure.Id + "/name", "{\"name\":\"" + padding + "\"}", csrfToken);
        var budget = await SendJsonAsync(browser, HttpMethod.Put, "/api/categories/" + leisure.Id + "/budget", "{\"monthlyBudgetInCents\":3000,\"bourrage\":\"" + padding + "\"}", csrfToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, creation.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rename.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, budget.StatusCode);
        var responseText = await creation.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Request body too large", responseText);
        Assert.DoesNotContain("1024", responseText);
        Assert.Equal(8, api.CategoryRepository.SavedCategories.Count);
        Assert.Equal("Loisirs", leisure.Name);
        Assert.Null(leisure.MonthlyBudgetInCents);
    }

    // La plus grosse requête valable, le plus long nom écrit en \uXXXX, passe sur le vrai serveur.
    // Ce test prouve aussi que la session et le jeton anti-CSRF marchent en vrai HTTPS, en mode production.
    [Fact]
    public async Task BiggestValidCategoryRequestPassesOverRealHttps()
    {
        var browser = CreateBrowser();
        var csrfToken = await LogInWithCategoriesAsync(browser);
        var escapedEmoji = "\\" + "ud83d" + "\\" + "ude00";
        var json = "{\"name\":\"" + string.Concat(Enumerable.Repeat(escapedEmoji, CategoryRules.MaximumNameLength)) + "\"}";

        var response = await SendJsonAsync(browser, HttpMethod.Post, "/api/categories", json, csrfToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(9, api.CategoryRepository.SavedCategories.Count);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/categories")).StatusCode);
    }
}
