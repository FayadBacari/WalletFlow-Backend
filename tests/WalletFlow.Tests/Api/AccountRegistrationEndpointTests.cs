using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la route d'inscription de bout en bout : validation, réponse, limite de débit, en-têtes.
public class AccountRegistrationEndpointTests
{
    [Fact]
    public async Task AnswerIsIdenticalWhetherTheAddressIsFreeOrAlreadyUsed()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();
        var requestSentTwiceWithTheSameAddress = new { email = "visiteur@exemple.test", password = ValidPasswordChosenInTheseTests };

        var answerForTheAddressStillFree = await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentTwiceWithTheSameAddress);
        var answerForTheAddressAlreadyUsed = await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentTwiceWithTheSameAddress);

        Assert.Equal(HttpStatusCode.OK, answerForTheAddressStillFree.StatusCode);
        Assert.Equal(HttpStatusCode.OK, answerForTheAddressAlreadyUsed.StatusCode);
        Assert.Equal(
            await answerForTheAddressStillFree.Content.ReadAsStringAsync(),
            await answerForTheAddressAlreadyUsed.Content.ReadAsStringAsync());

        // Seul l'e-mail change : un lien de confirmation, puis un simple avertissement.
        Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers);
        var emailsSentToTheVisitorAddress = apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails;
        Assert.Equal(2, emailsSentToTheVisitorAddress.Count);
        Assert.Contains("confirm-email#token=", emailsSentToTheVisitorAddress[0].Body);
        Assert.DoesNotContain("confirm-email", emailsSentToTheVisitorAddress[1].Body);
    }

    [Fact]
    public async Task PasswordBreakingTheRulesIsRefusedWithTheRuleToRespect()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        var answerToTheRequestWithWeakPassword = await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = "trop-court" });

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestWithWeakPassword.StatusCode);
        var jsonDocumentOfTheErrorAnswer = JsonDocument.Parse(await answerToTheRequestWithWeakPassword.Content.ReadAsStringAsync());
        var errorsListedInTheErrorAnswer = jsonDocumentOfTheErrorAnswer.RootElement.GetProperty("errors");
        Assert.Equal(AccountRegistrationService.ErrorMessageForPasswordBreakingTheRules, errorsListedInTheErrorAnswer.GetProperty("Password")[0].GetString());
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Empty(apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails);
    }

    // Un champ absent, null ou vide reçoit un message clair en français, jamais une erreur 500.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"email\":null,\"password\":null}")]
    [InlineData("{\"email\":\"\",\"password\":\"\"}")]
    [InlineData("{\"email\":\"   \",\"password\":\"   \"}")]
    public async Task RequestWithoutEmailAndPasswordIsRefusedWithBothErrors(string bodyOfTheRequestWithoutAnyUsableField)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        var answerToTheRequestWithoutAnyField = await clientThatCallsTheApiInMemory.PostAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new StringContent(bodyOfTheRequestWithoutAnyUsableField, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestWithoutAnyField.StatusCode);
        var jsonDocumentOfTheErrorAnswer = JsonDocument.Parse(await answerToTheRequestWithoutAnyField.Content.ReadAsStringAsync());
        var errorsListedInTheErrorAnswer = jsonDocumentOfTheErrorAnswer.RootElement.GetProperty("errors");
        Assert.Equal("L'adresse e-mail est obligatoire.", errorsListedInTheErrorAnswer.GetProperty("Email")[0].GetString());
        Assert.Equal("Le mot de passe est obligatoire.", errorsListedInTheErrorAnswer.GetProperty("Password")[0].GetString());
    }

    // SS §4 : seuls les champs prévus sont lus. Ajouter un champ à la requête ne donne aucun droit.
    [Fact]
    public async Task ExtraFieldsSentByTheVisitorCannotChooseRoleOrConfirmation()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        var answerToTheRequestWithExtraFields = await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new
            {
                email = "visiteur@exemple.test",
                password = ValidPasswordChosenInTheseTests,
                role = "Administrator",
                isEmailConfirmed = true
            });

        Assert.Equal(HttpStatusCode.OK, answerToTheRequestWithExtraFields.StatusCode);
        var userSavedFromTheRequestWithExtraFields = Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Equal(UserRole.User, userSavedFromTheRequestWithExtraFields.Role);
        Assert.False(userSavedFromTheRequestWithExtraFields.IsEmailConfirmed);
    }

    // SS §6 : 5 inscriptions par heure et par adresse IP, puis « trop de requêtes » (429).
    [Fact]
    public async Task SixthRegistrationFromTheSameAddressIsRefusedWithTooManyRequests()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        for (var numberOfTheRegistrationBeingSent = 1; numberOfTheRegistrationBeingSent <= 5; numberOfTheRegistrationBeingSent++)
        {
            var answerToTheAllowedRegistrationRequest = await clientThatCallsTheApiInMemory.PostAsJsonAsync(
                AddressOfTheAccountRegistrationEndpoint,
                new { email = "visiteur" + numberOfTheRegistrationBeingSent + "@exemple.test", password = ValidPasswordChosenInTheseTests });
            Assert.Equal(HttpStatusCode.OK, answerToTheAllowedRegistrationRequest.StatusCode);
        }

        var answerToTheSixthRegistrationRequest = await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur6@exemple.test", password = ValidPasswordChosenInTheseTests });

        Assert.Equal(HttpStatusCode.TooManyRequests, answerToTheSixthRegistrationRequest.StatusCode);
        Assert.Equal(5, apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers.Count);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheSixthRegistrationRequest);
    }

    // SS §6 : la limite compte les requêtes de chaque adresse IP séparément.
    [Fact]
    public async Task VisitorFromAnotherAddressIsNotBlockedByTheLimitOfTheFirstOne()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientCallingFromTheFirstAddress = apiStartedInMemoryForThisTest.CreateClient();
        clientCallingFromTheFirstAddress.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.1");
        var clientCallingFromTheSecondAddress = apiStartedInMemoryForThisTest.CreateClient();
        clientCallingFromTheSecondAddress.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.2");
        var requestSentByBothVisitorsOfThisTest = new { email = "visiteur@exemple.test", password = ValidPasswordChosenInTheseTests };

        for (var numberOfTheRegistrationBeingSent = 1; numberOfTheRegistrationBeingSent <= 5; numberOfTheRegistrationBeingSent++)
        {
            await clientCallingFromTheFirstAddress.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentByBothVisitorsOfThisTest);
        }

        var sixthAnswerToTheFirstAddress = await clientCallingFromTheFirstAddress.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentByBothVisitorsOfThisTest);
        var firstAnswerToTheSecondAddress = await clientCallingFromTheSecondAddress.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentByBothVisitorsOfThisTest);

        Assert.Equal(HttpStatusCode.TooManyRequests, sixthAnswerToTheFirstAddress.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstAnswerToTheSecondAddress.StatusCode);
    }

    // SS §6 : fige la limite écrite dans la stratégie. Le test ne peut pas attendre une heure pour la vérifier.
    [Fact]
    public void SignupLimitFollowsTheValuesOfTheSecurityStrategy()
    {
        var optionsOfTheSignupRequestsLimit = RateLimitingConfiguration.CreateOptionsOfTheSignupRequestsLimit();

        Assert.Equal(5, optionsOfTheSignupRequestsLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromHours(1), optionsOfTheSignupRequestsLimit.Window);
    }

    // La description de l'API est publiée en développement (voir le test du mode production).
    [Fact]
    public async Task OpenApiDescriptionIsPublishedInDevelopment()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        var answerToTheOpenApiRequest = await clientThatCallsTheApiInMemory.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, answerToTheOpenApiRequest.StatusCode);
    }

    // SS §6 : chaque réponse porte les en-têtes de sécurité, avec les valeurs exactes de la stratégie.
    [Fact]
    public async Task RegistrationAnswerCarriesTheFourSecurityHeaders()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        var answerToCheckForSecurityHeaders = await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = ValidPasswordChosenInTheseTests });

        Assert.Equal(HttpStatusCode.OK, answerToCheckForSecurityHeaders.StatusCode);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToCheckForSecurityHeaders);
    }

    // SS §6 : une requête refusée reçoit les mêmes en-têtes de sécurité.
    [Fact]
    public async Task RefusedRegistrationAnswerAlsoCarriesTheFourSecurityHeaders()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        var answerToTheRefusedRegistrationRequest = await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = "trop-court" });

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRefusedRegistrationRequest.StatusCode);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheRefusedRegistrationRequest);
    }

    // Le caractère NUL, que PostgreSQL ne sait pas enregistrer, est écarté avant d'arriver à la base.
    [Fact]
    public async Task EmailAddressContainingNullCharacterIsRefusedBeforeReachingTheDatabase()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        var answerToTheRequestWithNullCharacter = await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "\"a\\\u0000\"@exemple.fr", password = ValidPasswordChosenInTheseTests });

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestWithNullCharacter.StatusCode);
        var jsonDocumentOfTheErrorAnswer = JsonDocument.Parse(await answerToTheRequestWithNullCharacter.Content.ReadAsStringAsync());
        var errorsListedInTheErrorAnswer = jsonDocumentOfTheErrorAnswer.RootElement.GetProperty("errors");
        Assert.True(errorsListedInTheErrorAnswer.TryGetProperty("Email", out _));
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Empty(apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails);
    }
}
