using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Controllers;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie le format unique des erreurs, en mode production : un titre en français,
// des clés stables, et jamais un nom interne du code ni un texte du visiteur (SS §10).
public class ApiErrorFormatTests
{
    // Vérifie le format commun d'une erreur et renvoie le texte de la réponse.
    private static async Task<string> AssertFrenchProblemTitleAsync(
        HttpResponseMessage response,
        HttpStatusCode statusCode,
        string frenchTitle)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var responseText = await response.Content.ReadAsStringAsync();
        var jsonDocument = JsonDocument.Parse(responseText);
        Assert.Equal(frenchTitle, jsonDocument.RootElement.GetProperty("title").GetString());
        Assert.Equal((int)statusCode, jsonDocument.RootElement.GetProperty("status").GetInt32());

        return responseText;
    }

    [Fact]
    public async Task UnknownWebsiteAddressOfTheApiGivesFrenchProblem()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = productionApi.CreateClient();

        var response = await client.GetAsync("/adresse-inconnue");

        await AssertFrenchProblemTitleAsync(response, HttpStatusCode.NotFound, "Cette adresse n'existe pas.");
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    [Fact]
    public async Task WrongHttpMethodGivesFrenchProblem()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = productionApi.CreateClient();

        var response = await client.GetAsync(RegistrationEndpoint);

        await AssertFrenchProblemTitleAsync(response, HttpStatusCode.MethodNotAllowed, "Cette action n'est pas possible à cette adresse.");
    }

    [Fact]
    public async Task WrongContentTypeGivesFrenchProblem()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = productionApi.CreateClient();

        var response = await client.PostAsync(
            RegistrationEndpoint,
            new StringContent("email=visiteur@exemple.test", Encoding.UTF8, "text/plain"));

        await AssertFrenchProblemTitleAsync(response, HttpStatusCode.UnsupportedMediaType, "Le format de la requête n'est pas accepté.");
    }

    // Le dépassement d'une limite de débit n'a pas de contenu : il reçoit quand même le format commun.
    [Fact]
    public async Task RequestOverTheRateLimitGivesFrenchProblem()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = productionApi.CreateClient();

        for (var registrationNumber = 1; registrationNumber <= 5; registrationNumber++)
        {
            await client.PostAsJsonAsync(
                RegistrationEndpoint,
                new { email = "visiteur" + registrationNumber + "@exemple.test", password = ValidPassword });
        }

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur6@exemple.test", password = ValidPassword });

        await AssertFrenchProblemTitleAsync(response, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");
    }

    [Fact]
    public async Task UnexpectedFailureOfTheApiGivesFrenchProblem()
    {
        using var productionApi = new ApiFactoryInProduction();
        productionApi.AccountRepository.IsDown = true;
        var client = productionApi.CreateClient();

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = ValidPassword });

        await AssertFrenchProblemTitleAsync(response, HttpStatusCode.InternalServerError, "Une erreur est survenue. Réessaie plus tard.");
    }

    // Le champ refusé garde sa clé stable et son message en français.
    [Fact]
    public async Task RefusedFieldKeepsItsStableKeyAndItsFrenchMessage()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = productionApi.CreateClient();

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "adresse-sans-arobase", password = ValidPassword });

        var responseText = await AssertFrenchProblemTitleAsync(response, HttpStatusCode.BadRequest, ApiErrorConfiguration.RefusedRequestTitle);
        var errors = JsonDocument.Parse(responseText).RootElement.GetProperty("errors");
        Assert.Equal("L'adresse e-mail n'est pas valide.", errors.GetProperty("Email")[0].GetString());
    }

    // Une requête illisible donne toujours la même erreur, quel que soit son contenu : aucune clé ne dépend du visiteur.
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{\"email\":")]
    [InlineData("\"texte\"")]
    [InlineData("123")]
    [InlineData("{\"email\":1,\"password\":2}")]
    [InlineData("{\"<script>alert(1)</script>\":}")]
    [InlineData("{\"email\":\"visiteur@exemple.test\",\"<img src=x onerror=alert(1)>\":[")]
    [InlineData("{\"email\":\"visiteur@exemple.test\",\"password\":\"Aa1!\\ud83dbcdefghij\"}")] // moitié d'emoji seule
    public async Task UnreadableRequestAlwaysGivesTheSameGenericError(string unreadableBody)
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = productionApi.CreateClient();

        var response = await client.PostAsync(
            RegistrationEndpoint,
            new StringContent(unreadableBody, Encoding.UTF8, "application/json"));

        var responseText = await AssertFrenchProblemTitleAsync(response, HttpStatusCode.BadRequest, ApiErrorConfiguration.RefusedRequestTitle);

        var errors = JsonDocument.Parse(responseText).RootElement.GetProperty("errors");
        var errorProperty = Assert.Single(errors.EnumerateObject());
        Assert.Equal(ApiErrorConfiguration.GeneralErrorsKey, errorProperty.Name);
        var errorMessage = Assert.Single(errorProperty.Value.EnumerateArray());
        Assert.Equal(ApiErrorConfiguration.UnreadableRequestMessage, errorMessage.GetString());

        Assert.DoesNotContain("<script>", responseText);
        Assert.DoesNotContain("<img", responseText);
        Assert.DoesNotContain("WalletFlow", responseText);
        Assert.DoesNotContain(ReadParameterName(typeof(AccountRegistrationController), nameof(AccountRegistrationController.Register)), responseText);
        Assert.DoesNotContain("System.Text.Json", responseText);
        Assert.Empty(productionApi.AccountRepository.SavedUsers);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    // Un champ d'un mauvais type n'est pas un champ manquant : il donne l'erreur générique, sans citer son chemin JSON.
    [Fact]
    public async Task ConfirmationRouteUsesTheSameFormatForUnreadableRequest()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = productionApi.CreateClient();

        var response = await client.PostAsync(
            ConfirmationEndpoint,
            new StringContent("{\"token\":[],\"password\":{}}", Encoding.UTF8, "application/json"));

        var responseText = await AssertFrenchProblemTitleAsync(response, HttpStatusCode.BadRequest, ApiErrorConfiguration.RefusedRequestTitle);
        Assert.DoesNotContain("$", responseText);
        Assert.DoesNotContain(ReadParameterName(typeof(AccountConfirmationController), nameof(AccountConfirmationController.ConfirmAccount)), responseText);
        Assert.Contains(ApiErrorConfiguration.UnreadableRequestMessage, responseText);
    }

    // Nom que le code donne au paramètre d'une action : un détail interne, qui ne doit jamais sortir dans une réponse.
    // Il est lu dans le code : le test reste juste si ce nom change.
    private static string ReadParameterName(Type controllerType, string actionName)
    {
        var action = controllerType.GetMethod(actionName)
            ?? throw new InvalidOperationException("L'action " + actionName + " n'existe pas.");
        return action.GetParameters().Single().Name
            ?? throw new InvalidOperationException("Le paramètre de l'action " + actionName + " n'a pas de nom.");
    }
}
