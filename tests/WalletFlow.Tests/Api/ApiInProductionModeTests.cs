using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie ce qu'un vrai visiteur reçoit en production : erreurs génériques et HSTS (SS §6 et §10).
public class ApiInProductionModeTests
{
    // Une panne imprévue donne une erreur générique : le détail ne va que dans les journaux (SS §10).
    [Fact]
    public async Task UnexpectedFailureIsAnsweredWithoutAnyTechnicalDetail()
    {
        using var productionApi = new ApiFactoryInProduction();
        productionApi.AccountRepository.IsDown = true;
        var client = productionApi.CreateClient();

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = ValidPassword });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeUserAccountRepository.FailureDetail, responseText);
        Assert.DoesNotContain(nameof(FakeUserAccountRepository), responseText);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    // HSTS : le navigateur n'utilise plus que HTTPS pendant un an, sous-domaines compris (SS §6).
    [Fact]
    public async Task AnswerOverHttpsCarriesTheStrictTransportSecurityHeader()
    {
        using var productionApi = new ApiFactoryInProduction();

        // ASP.NET Core n'envoie jamais HSTS à localhost : le client utilise donc un vrai nom de domaine, en HTTPS.
        var client = productionApi.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://walletflow.test") });

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = ValidPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SecurityHeadersAssertions.AssertHeaderValue(
            response,
            "Strict-Transport-Security",
            "max-age=31536000; includeSubDomains");
    }

    // Une requête en HTTP est redirigée vers HTTPS, et rien n'est enregistré (SS §7).
    [Fact]
    public async Task RequestOverHttpIsRedirectedToHttpsWithoutSavingAnything()
    {
        using var productionApi = new ApiFactoryInProduction();
        using var httpsApi = productionApi.WithWebHostBuilder(apiBuilder =>
            apiBuilder.UseSetting("https_port", "443"));
        var client = httpsApi.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://walletflow.test") });

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = ValidPassword });

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://walletflow.test" + RegistrationEndpoint, response.Headers.Location!.ToString());
        Assert.Empty(productionApi.AccountRepository.SavedUsers);
    }

    // La description de l'API n'est publiée qu'en développement.
    [Fact]
    public async Task OpenApiDescriptionIsNotPublishedInProduction()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = productionApi.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // L'API refuse de démarrer si un réglage obligatoire manque, au lieu de tourner à moitié configurée.
    [Theory]
    [InlineData("ConnectionStrings:WalletFlowDatabase", "WalletFlowDatabase")]
    [InlineData("LocalEmailsFolder", "LocalEmailsFolder")]
    [InlineData("PublicWebsiteAddress", "PublicWebsiteAddress")]
    [InlineData("TwoFactorSecretEncryptionKey", "TwoFactorSecretEncryptionKey")]
    public void ApiRefusesToStartWithoutItsRequiredSetting(string emptySettingName, string text)
    {
        using var api = new ApiFactoryWithEmptySetting(emptySettingName);

        var exception = Assert.Throws<InvalidOperationException>(() => api.CreateClient());

        Assert.Contains(text, exception.Message);
    }

    // Une clé absente ou vide reçoit le message qui dit quoi faire : elle ne doit pas arriver jusqu'au contrôle de sa forme.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApiRefusesToStartWithoutTheEncryptionKeyAndSaysThatItIsMissing(string? settingValue)
    {
        using var productionApi = new ApiFactoryInProduction();
        using var apiWithoutKey = productionApi.WithWebHostBuilder(apiBuilder =>
            apiBuilder.UseSetting("TwoFactorSecretEncryptionKey", settingValue));

        var exception = Assert.Throws<InvalidOperationException>(() => apiWithoutKey.CreateClient());

        Assert.Contains("« TwoFactorSecretEncryptionKey » est absent", exception.Message);
    }

    // Une clé trop courte ou mal écrite empêche aussi de démarrer, et le message ne la cite pas : il part dans les journaux (SS §7).
    [Theory]
    [InlineData("cle-trop-courte")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    public void ApiRefusesToStartWithABadEncryptionKeyWithoutQuotingIt(string badKey)
    {
        using var productionApi = new ApiFactoryInProduction();
        using var apiWithBadKey = productionApi.WithWebHostBuilder(apiBuilder =>
            apiBuilder.UseSetting("TwoFactorSecretEncryptionKey", badKey));

        var exception = Assert.Throws<InvalidOperationException>(() => apiWithBadKey.CreateClient());

        Assert.Contains("TwoFactorSecretEncryptionKey", exception.Message);
        Assert.DoesNotContain(badKey, exception.Message);
    }
}
