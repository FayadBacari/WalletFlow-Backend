using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie le format unique des erreurs, en mode production : un titre en français,
// des clés stables, et jamais un nom interne du code ni un texte du visiteur (SS §10).
public class ApiErrorFormatTests
{
    // Vérifie le format commun d'une erreur et renvoie le texte de la réponse.
    private static async Task<string> CheckThatAnswerIsProblemWithThisFrenchTitleAsync(
        HttpResponseMessage answerOfTheApiToCheck,
        HttpStatusCode statusCodeExpectedInTheAnswer,
        string frenchTitleExpectedInTheAnswer)
    {
        Assert.Equal(statusCodeExpectedInTheAnswer, answerOfTheApiToCheck.StatusCode);
        Assert.Equal("application/problem+json", answerOfTheApiToCheck.Content.Headers.ContentType?.MediaType);

        var textOfTheAnswerToCheck = await answerOfTheApiToCheck.Content.ReadAsStringAsync();
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(textOfTheAnswerToCheck);
        Assert.Equal(frenchTitleExpectedInTheAnswer, jsonDocumentOfTheApiAnswer.RootElement.GetProperty("title").GetString());
        Assert.Equal((int)statusCodeExpectedInTheAnswer, jsonDocumentOfTheApiAnswer.RootElement.GetProperty("status").GetInt32());

        return textOfTheAnswerToCheck;
    }

    [Fact]
    public async Task UnknownWebsiteAddressOfTheApiGivesFrenchProblem()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();

        var answerToTheUnknownWebsiteAddress = await clientThatCallsTheApiInProductionMode.GetAsync("/adresse-inconnue");

        await CheckThatAnswerIsProblemWithThisFrenchTitleAsync(answerToTheUnknownWebsiteAddress, HttpStatusCode.NotFound, "Cette adresse n'existe pas.");
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheUnknownWebsiteAddress);
    }

    [Fact]
    public async Task WrongHttpMethodGivesFrenchProblem()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();

        var answerToTheWrongHttpMethodRequest = await clientThatCallsTheApiInProductionMode.GetAsync(AddressOfTheAccountRegistrationEndpoint);

        await CheckThatAnswerIsProblemWithThisFrenchTitleAsync(answerToTheWrongHttpMethodRequest, HttpStatusCode.MethodNotAllowed, "Cette action n'est pas possible à cette adresse.");
    }

    [Fact]
    public async Task WrongContentTypeGivesFrenchProblem()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();

        var answerToTheWrongContentType = await clientThatCallsTheApiInProductionMode.PostAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new StringContent("email=visiteur@exemple.test", Encoding.UTF8, "text/plain"));

        await CheckThatAnswerIsProblemWithThisFrenchTitleAsync(answerToTheWrongContentType, HttpStatusCode.UnsupportedMediaType, "Le format de la requête n'est pas accepté.");
    }

    // Le dépassement d'une limite de débit n'a pas de contenu : il reçoit quand même le format commun.
    [Fact]
    public async Task RequestOverTheRateLimitGivesFrenchProblem()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();

        for (var numberOfTheRegistrationBeingSent = 1; numberOfTheRegistrationBeingSent <= 5; numberOfTheRegistrationBeingSent++)
        {
            await clientThatCallsTheApiInProductionMode.PostAsJsonAsync(
                AddressOfTheAccountRegistrationEndpoint,
                new { email = "visiteur" + numberOfTheRegistrationBeingSent + "@exemple.test", password = ValidPasswordChosenInTheseTests });
        }

        var answerToRequestOverTheRateLimit = await clientThatCallsTheApiInProductionMode.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur6@exemple.test", password = ValidPasswordChosenInTheseTests });

        await CheckThatAnswerIsProblemWithThisFrenchTitleAsync(answerToRequestOverTheRateLimit, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");
    }

    [Fact]
    public async Task UnexpectedFailureOfTheApiGivesFrenchProblem()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        apiStartedLikeInProductionForThisTest.RepositoryThatKeepsAccountsInMemory.DatabaseIsDownDuringThisTest = true;
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();

        var answerDuringTheSimulatedDatabaseFailure = await clientThatCallsTheApiInProductionMode.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = ValidPasswordChosenInTheseTests });

        await CheckThatAnswerIsProblemWithThisFrenchTitleAsync(answerDuringTheSimulatedDatabaseFailure, HttpStatusCode.InternalServerError, "Une erreur est survenue. Réessaie plus tard.");
    }

    // Le champ refusé garde sa clé stable et son message en français.
    [Fact]
    public async Task RefusedFieldKeepsItsStableKeyAndItsFrenchMessage()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();

        var answerToTheRefusedRegistrationRequest = await clientThatCallsTheApiInProductionMode.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "adresse-sans-arobase", password = ValidPasswordChosenInTheseTests });

        var textOfTheAnswerToRefusedRequest = await CheckThatAnswerIsProblemWithThisFrenchTitleAsync(answerToTheRefusedRegistrationRequest, HttpStatusCode.BadRequest, ApiErrorConfiguration.TitleOfEveryRefusedRequestAnswer);
        var errorsListedInTheRefusedAnswer = JsonDocument.Parse(textOfTheAnswerToRefusedRequest).RootElement.GetProperty("errors");
        Assert.Equal("L'adresse e-mail n'est pas valide.", errorsListedInTheRefusedAnswer.GetProperty("Email")[0].GetString());
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
    public async Task UnreadableRequestAlwaysGivesTheSameGenericError(string unreadableBodyOfTheRegistrationRequest)
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();

        var answerToTheUnreadableRegistrationRequest = await clientThatCallsTheApiInProductionMode.PostAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new StringContent(unreadableBodyOfTheRegistrationRequest, Encoding.UTF8, "application/json"));

        var textOfTheAnswerToUnreadableRequest = await CheckThatAnswerIsProblemWithThisFrenchTitleAsync(answerToTheUnreadableRegistrationRequest, HttpStatusCode.BadRequest, ApiErrorConfiguration.TitleOfEveryRefusedRequestAnswer);

        var errorsListedInTheRefusedAnswer = JsonDocument.Parse(textOfTheAnswerToUnreadableRequest).RootElement.GetProperty("errors");
        var propertyOfTheOnlyErrorInAnswer = Assert.Single(errorsListedInTheRefusedAnswer.EnumerateObject());
        Assert.Equal(ApiErrorConfiguration.KeyOfTheErrorsOutsideTheRequestFields, propertyOfTheOnlyErrorInAnswer.Name);
        var messageOfTheOnlyErrorInAnswer = Assert.Single(propertyOfTheOnlyErrorInAnswer.Value.EnumerateArray());
        Assert.Equal(ApiErrorConfiguration.MessageForRequestThatCannotBeRead, messageOfTheOnlyErrorInAnswer.GetString());

        Assert.DoesNotContain("<script>", textOfTheAnswerToUnreadableRequest);
        Assert.DoesNotContain("<img", textOfTheAnswerToUnreadableRequest);
        Assert.DoesNotContain("WalletFlow", textOfTheAnswerToUnreadableRequest);
        Assert.DoesNotContain("requestSentByTheVisitorToRegister", textOfTheAnswerToUnreadableRequest);
        Assert.DoesNotContain("System.Text.Json", textOfTheAnswerToUnreadableRequest);
        Assert.Empty(apiStartedLikeInProductionForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheUnreadableRegistrationRequest);
    }

    // Un champ d'un mauvais type n'est pas un champ manquant : il donne l'erreur générique, sans citer son chemin JSON.
    [Fact]
    public async Task ConfirmationRouteUsesTheSameFormatForUnreadableRequest()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();

        var answerToTheUnreadableConfirmationRequest = await clientThatCallsTheApiInProductionMode.PostAsync(
            AddressOfTheAccountConfirmationEndpoint,
            new StringContent("{\"token\":[],\"password\":{}}", Encoding.UTF8, "application/json"));

        var textOfTheAnswerToUnreadableRequest = await CheckThatAnswerIsProblemWithThisFrenchTitleAsync(answerToTheUnreadableConfirmationRequest, HttpStatusCode.BadRequest, ApiErrorConfiguration.TitleOfEveryRefusedRequestAnswer);
        Assert.DoesNotContain("$", textOfTheAnswerToUnreadableRequest);
        Assert.DoesNotContain("requestSentByTheVisitorToConfirm", textOfTheAnswerToUnreadableRequest);
        Assert.Contains(ApiErrorConfiguration.MessageForRequestThatCannotBeRead, textOfTheAnswerToUnreadableRequest);
    }
}
