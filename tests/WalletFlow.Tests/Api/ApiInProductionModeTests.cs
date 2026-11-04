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
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        apiStartedLikeInProductionForThisTest.RepositoryThatKeepsAccountsInMemory.DatabaseIsDownDuringThisTest = true;
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();

        var answerToTheRequestDuringTheDatabaseFailure = await clientThatCallsTheApiInProductionMode.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = ValidPasswordChosenInTheseTests });

        Assert.Equal(HttpStatusCode.InternalServerError, answerToTheRequestDuringTheDatabaseFailure.StatusCode);
        var textOfTheAnswerDuringTheDatabaseFailure = await answerToTheRequestDuringTheDatabaseFailure.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeUserAccountRepository.TechnicalDetailOfTheSimulatedDatabaseFailure, textOfTheAnswerDuringTheDatabaseFailure);
        Assert.DoesNotContain(nameof(FakeUserAccountRepository), textOfTheAnswerDuringTheDatabaseFailure);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheRequestDuringTheDatabaseFailure);
    }

    // HSTS : le navigateur n'utilise plus que HTTPS pendant un an, sous-domaines compris (SS §6).
    [Fact]
    public async Task AnswerOverHttpsCarriesTheStrictTransportSecurityHeader()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();

        // ASP.NET Core n'envoie jamais HSTS à localhost : le client utilise donc un vrai nom de domaine, en HTTPS.
        var clientThatCallsTheApiOverHttps = apiStartedLikeInProductionForThisTest.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://walletflow.test") });

        var answerToTheRequestSentOverHttps = await clientThatCallsTheApiOverHttps.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = ValidPasswordChosenInTheseTests });

        Assert.Equal(HttpStatusCode.OK, answerToTheRequestSentOverHttps.StatusCode);
        SecurityHeadersAssertions.CheckThatHeaderOfTheAnswerHasThisExactValue(
            answerToTheRequestSentOverHttps,
            "Strict-Transport-Security",
            "max-age=31536000; includeSubDomains");
    }

    // Une requête en HTTP est redirigée vers HTTPS, et rien n'est enregistré (SS §7).
    [Fact]
    public async Task RequestOverHttpIsRedirectedToHttpsWithoutSavingAnything()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        using var apiWithItsHttpsPortConfigured = apiStartedLikeInProductionForThisTest.WithWebHostBuilder(builderOfTheApiWithItsHttpsPort =>
            builderOfTheApiWithItsHttpsPort.UseSetting("https_port", "443"));
        var clientThatDoesNotFollowRedirections = apiWithItsHttpsPortConfigured.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://walletflow.test") });

        var answerToTheRequestSentOverHttp = await clientThatDoesNotFollowRedirections.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = ValidPasswordChosenInTheseTests });

        Assert.Equal(HttpStatusCode.TemporaryRedirect, answerToTheRequestSentOverHttp.StatusCode);
        Assert.Equal("https://walletflow.test" + AddressOfTheAccountRegistrationEndpoint, answerToTheRequestSentOverHttp.Headers.Location!.ToString());
        Assert.Empty(apiStartedLikeInProductionForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers);
    }

    // La description de l'API n'est publiée qu'en développement.
    [Fact]
    public async Task OpenApiDescriptionIsNotPublishedInProduction()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();

        var answerToTheOpenApiRequest = await clientThatCallsTheApiInProductionMode.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, answerToTheOpenApiRequest.StatusCode);
    }

    // L'API refuse de démarrer si un réglage obligatoire manque, au lieu de tourner à moitié configurée.
    [Theory]
    [InlineData("ConnectionStrings:WalletFlowDatabase", "WalletFlowDatabase")]
    [InlineData("LocalEmailsFolder", "LocalEmailsFolder")]
    [InlineData("PublicWebsiteAddress", "PublicWebsiteAddress")]
    public void ApiRefusesToStartWithoutItsRequiredSetting(string nameOfTheSettingLeftEmptyForThisTest, string textExpectedInTheStartupError)
    {
        using var apiStartedWithoutOneRequiredSetting = new ApiFactoryWithOneEmptySetting(nameOfTheSettingLeftEmptyForThisTest);

        var errorRaisedByTheApiStartup = Assert.Throws<InvalidOperationException>(() => apiStartedWithoutOneRequiredSetting.CreateClient());

        Assert.Contains(textExpectedInTheStartupError, errorRaisedByTheApiStartup.Message);
    }
}
