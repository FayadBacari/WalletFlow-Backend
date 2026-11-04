using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Controllers;
using WalletFlow.Api.Configuration;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la route d'inscription de bout en bout : validation, réponse, limite de débit, en-têtes.
public class AccountRegistrationEndpointTests
{
    // Crée un client dont les requêtes viennent de cette adresse IP.
    private static HttpClient CreateClientCallingFromThisIpAddress(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest, string ipAddressOfTheVisitorForThisTest)
    {
        var clientCallingFromThisIpAddress = apiStartedInMemoryForThisTest.CreateClient();
        clientCallingFromThisIpAddress.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, ipAddressOfTheVisitorForThisTest);

        return clientCallingFromThisIpAddress;
    }

    // La réponse est la même dans les trois états de l'adresse : seul l'e-mail change (SS §2).
    [Fact]
    public async Task AnswerIsIdenticalWhetherTheAddressIsFreeUnconfirmedOrConfirmed()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();
        var requestSentThreeTimesWithTheSameAddress = new { email = "visiteur@exemple.test", password = ValidPasswordChosenInTheseTests };

        var answerForTheAddressStillFree = await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentThreeTimesWithTheSameAddress);
        var answerForTheAddressNotYetConfirmed = await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentThreeTimesWithTheSameAddress);
        apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers[0].IsEmailConfirmed = true;
        var answerForTheAddressAlreadyConfirmed = await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentThreeTimesWithTheSameAddress);

        Assert.Equal(HttpStatusCode.OK, answerForTheAddressStillFree.StatusCode);
        Assert.Equal(HttpStatusCode.OK, answerForTheAddressNotYetConfirmed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, answerForTheAddressAlreadyConfirmed.StatusCode);
        var textOfTheAnswerForTheFreeAddress = await answerForTheAddressStillFree.Content.ReadAsStringAsync();
        Assert.Equal(textOfTheAnswerForTheFreeAddress, await answerForTheAddressNotYetConfirmed.Content.ReadAsStringAsync());
        Assert.Equal(textOfTheAnswerForTheFreeAddress, await answerForTheAddressAlreadyConfirmed.Content.ReadAsStringAsync());

        // Seul l'e-mail change : un lien de confirmation, un nouveau lien, puis un simple avertissement.
        Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers);
        var emailsSentToTheVisitorAddress = apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails;
        Assert.Equal(3, emailsSentToTheVisitorAddress.Count);
        Assert.Contains("confirm-email#token=", emailsSentToTheVisitorAddress[0].Body);
        Assert.Contains("confirm-email#token=", emailsSentToTheVisitorAddress[1].Body);
        Assert.DoesNotContain("confirm-email", emailsSentToTheVisitorAddress[2].Body);
    }

    // SS §6 : 3 inscriptions par heure pour une même adresse e-mail, même depuis plusieurs adresses IP.
    [Fact]
    public async Task FourthRegistrationOfTheSameAddressIsRefusedEvenFromAnotherIpAddress()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var requestSentByEveryVisitorOfThisTest = new { email = "victime@exemple.test", password = ValidPasswordChosenInTheseTests };

        for (var numberOfTheRegistrationBeingSent = 1; numberOfTheRegistrationBeingSent <= 3; numberOfTheRegistrationBeingSent++)
        {
            var clientSendingTheAllowedRegistrationRequest = CreateClientCallingFromThisIpAddress(apiStartedInMemoryForThisTest, "203.0.113." + numberOfTheRegistrationBeingSent);
            var answerToTheAllowedRegistrationRequest = await clientSendingTheAllowedRegistrationRequest.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentByEveryVisitorOfThisTest);
            Assert.Equal(HttpStatusCode.OK, answerToTheAllowedRegistrationRequest.StatusCode);
        }

        var clientSendingTheFourthRegistrationRequest = CreateClientCallingFromThisIpAddress(apiStartedInMemoryForThisTest, "203.0.113.4");
        var answerToTheFourthRegistrationRequest = await clientSendingTheFourthRegistrationRequest.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentByEveryVisitorOfThisTest);

        Assert.Equal(HttpStatusCode.TooManyRequests, answerToTheFourthRegistrationRequest.StatusCode);
        Assert.Equal(3, apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Count);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheFourthRegistrationRequest);

        // Une heure plus tard, l'adresse est de nouveau acceptée.
        apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromHours(1));
        var clientSendingTheLaterRegistrationRequest = CreateClientCallingFromThisIpAddress(apiStartedInMemoryForThisTest, "203.0.113.5");
        var answerToTheLaterRegistrationRequest = await clientSendingTheLaterRegistrationRequest.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentByEveryVisitorOfThisTest);
        Assert.Equal(HttpStatusCode.OK, answerToTheLaterRegistrationRequest.StatusCode);
    }

    // GR Auth : si l'e-mail ne part pas, le visiteur est invité à réessayer, sans aucun détail technique.
    [Fact]
    public async Task EmailFailureGivesClearErrorAndTheNextRegistrationSendsNewLink()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var clientThatCallsTheApiInProductionMode = apiStartedLikeInProductionForThisTest.CreateClient();
        var requestSentTwiceWithTheSameAddress = new { email = "visiteur@exemple.test", password = ValidPasswordChosenInTheseTests };
        apiStartedLikeInProductionForThisTest.SenderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;

        var answerDuringTheSimulatedEmailFailure = await clientThatCallsTheApiInProductionMode.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentTwiceWithTheSameAddress);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answerDuringTheSimulatedEmailFailure.StatusCode);
        var textOfTheAnswerDuringTheEmailFailure = await answerDuringTheSimulatedEmailFailure.Content.ReadAsStringAsync();
        var jsonDocumentOfTheEmailFailure = JsonDocument.Parse(textOfTheAnswerDuringTheEmailFailure);
        Assert.Equal(AccountRegistrationController.MessageShownWhenTheEmailCouldNotBeSent, jsonDocumentOfTheEmailFailure.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("panne simulée", textOfTheAnswerDuringTheEmailFailure);
        Assert.DoesNotContain("visiteur@exemple.test", textOfTheAnswerDuringTheEmailFailure);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerDuringTheSimulatedEmailFailure);

        // Le journal désigne l'utilisateur par son identifiant, jamais par son adresse (SS §7).
        var userSavedDuringTheEmailFailure = Assert.Single(apiStartedLikeInProductionForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers);
        var completeTextOfTheTechnicalLog = string.Join("\n", apiStartedLikeInProductionForThisTest.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog);
        Assert.Contains(userSavedDuringTheEmailFailure.Id.ToString(), completeTextOfTheTechnicalLog);

        apiStartedLikeInProductionForThisTest.SenderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = false;
        var answerAfterTheEndOfEmailFailure = await clientThatCallsTheApiInProductionMode.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentTwiceWithTheSameAddress);

        Assert.Equal(HttpStatusCode.OK, answerAfterTheEndOfEmailFailure.StatusCode);
        Assert.Contains("confirm-email#token=", Assert.Single(apiStartedLikeInProductionForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails).Body);
    }

    // SS §7 : même au niveau le plus détaillé, le journal ne contient ni adresse, ni mot de passe, ni jeton.
    [Fact]
    public async Task TechnicalLogNeverContainsAddressPasswordOrToken()
    {
        using var apiLoggingEverythingForThisTest = new ApiFactoryLoggingEverything();
        var clientThatCallsTheApiInMemory = apiLoggingEverythingForThisTest.CreateClient();
        var requestSentWithTheSameAddress = new { email = "visiteur-du-journal@exemple.test", password = ValidPasswordChosenInTheseTests };

        await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentWithTheSameAddress);
        var tokenOfTheEmailConfirmationLink = ReadTokenWrittenInTheConfirmationLink(apiLoggingEverythingForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails[0].Body);
        await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheAccountConfirmationEndpoint, new { token = tokenOfTheEmailConfirmationLink, password = "Autre-Mot-De-Passe-2" });
        await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheAccountConfirmationEndpoint, new { token = tokenOfTheEmailConfirmationLink, password = ValidPasswordChosenInTheseTests });
        apiLoggingEverythingForThisTest.SenderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;
        await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheAccountRegistrationEndpoint, requestSentWithTheSameAddress);

        var messagesWrittenInTheTechnicalLog = apiLoggingEverythingForThisTest.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog;
        Assert.Contains(messagesWrittenInTheTechnicalLog, messageWrittenInTheTechnicalLog => messageWrittenInTheTechnicalLog.StartsWith("Debug"));

        var completeTextOfTheTechnicalLog = string.Join("\n", messagesWrittenInTheTechnicalLog);
        Assert.DoesNotContain("visiteur-du-journal@exemple.test", completeTextOfTheTechnicalLog);
        Assert.DoesNotContain(ValidPasswordChosenInTheseTests, completeTextOfTheTechnicalLog);
        Assert.DoesNotContain("Autre-Mot-De-Passe-2", completeTextOfTheTechnicalLog);
        Assert.DoesNotContain(tokenOfTheEmailConfirmationLink, completeTextOfTheTechnicalLog);
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
        var clientCallingFromTheFirstAddress = CreateClientCallingFromThisIpAddress(apiStartedInMemoryForThisTest, "203.0.113.1");
        var clientCallingFromTheSecondAddress = CreateClientCallingFromThisIpAddress(apiStartedInMemoryForThisTest, "203.0.113.2");

        for (var numberOfTheRegistrationBeingSent = 1; numberOfTheRegistrationBeingSent <= 5; numberOfTheRegistrationBeingSent++)
        {
            await clientCallingFromTheFirstAddress.PostAsJsonAsync(
                AddressOfTheAccountRegistrationEndpoint,
                new { email = "visiteur" + numberOfTheRegistrationBeingSent + "@exemple.test", password = ValidPasswordChosenInTheseTests });
        }

        var sixthAnswerToTheFirstAddress = await clientCallingFromTheFirstAddress.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur6@exemple.test", password = ValidPasswordChosenInTheseTests });
        var firstAnswerToTheSecondAddress = await clientCallingFromTheSecondAddress.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = "visiteur7@exemple.test", password = ValidPasswordChosenInTheseTests });

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
