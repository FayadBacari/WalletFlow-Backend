using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Controllers;
using WalletFlow.Application.UserAccounts;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la route de confirmation de bout en bout, avec le vrai hachage et les vrais jetons (GR Auth, SS §2).
public class AccountConfirmationEndpointTests
{
    private const string OtherPasswordChosenInTheseTests = "Autre-Mot-De-Passe-2";
    private const string EmailAddressRegisteredInTheseTests = "visiteur@exemple.test";

    // Envoie une demande d'inscription et renvoie le jeton du lien reçu par e-mail.
    private static async Task<string> RegisterVisitorThenReadTheTokenOfTheLinkAsync(
        ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest,
        HttpClient clientThatCallsTheApiInMemory,
        string passwordChosenByTheVisitorForThisTest)
    {
        var answerToTheVisitorRegistrationRequest = await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = EmailAddressRegisteredInTheseTests, password = passwordChosenByTheVisitorForThisTest });

        Assert.Equal(HttpStatusCode.OK, answerToTheVisitorRegistrationRequest.StatusCode);
        return ReadTokenWrittenInTheConfirmationLink(apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Last().Body);
    }

    // Envoie une demande de confirmation.
    private static async Task<HttpResponseMessage> SendAccountConfirmationRequestToTheApiAsync(
        HttpClient clientThatCallsTheApiInMemory,
        string tokenReadInTheConfirmationLink,
        string passwordTypedOnTheConfirmationPage)
    {
        return await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountConfirmationEndpoint,
            new { token = tokenReadInTheConfirmationLink, password = passwordTypedOnTheConfirmationPage });
    }

    // Lit le premier message d'erreur d'un champ dans la réponse.
    private static async Task<string?> ReadErrorMessageOfThisFieldAsync(HttpResponseMessage answerOfTheApiToRead, string nameOfTheFieldWithTheError)
    {
        var jsonDocumentOfTheErrorAnswer = JsonDocument.Parse(await answerOfTheApiToRead.Content.ReadAsStringAsync());

        return jsonDocumentOfTheErrorAnswer.RootElement.GetProperty("errors").GetProperty(nameOfTheFieldWithTheError)[0].GetString();
    }

    [Fact]
    public async Task RightLinkAndPasswordConfirmTheAccount()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();
        var tokenOfTheEmailConfirmationLink = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatCallsTheApiInMemory, ValidPasswordChosenInTheseTests);

        var answerToTheAccountConfirmationRequest = await SendAccountConfirmationRequestToTheApiAsync(clientThatCallsTheApiInMemory, tokenOfTheEmailConfirmationLink, ValidPasswordChosenInTheseTests);

        Assert.Equal(HttpStatusCode.OK, answerToTheAccountConfirmationRequest.StatusCode);
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheAccountConfirmationRequest.Content.ReadAsStringAsync());
        Assert.Equal(AccountConfirmationController.MessageSentWhenTheAccountIsConfirmed, jsonDocumentOfTheApiAnswer.RootElement.GetProperty("message").GetString());
        Assert.True(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers[0].IsEmailConfirmed);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheAccountConfirmationRequest);
    }

    [Fact]
    public async Task WrongPasswordIsRefusedOnItsFieldAndTheLinkStaysUsable()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();
        var tokenOfTheEmailConfirmationLink = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatCallsTheApiInMemory, ValidPasswordChosenInTheseTests);

        var answerWithTheWrongVisitorPassword = await SendAccountConfirmationRequestToTheApiAsync(clientThatCallsTheApiInMemory, tokenOfTheEmailConfirmationLink, OtherPasswordChosenInTheseTests);

        Assert.Equal(HttpStatusCode.BadRequest, answerWithTheWrongVisitorPassword.StatusCode);
        Assert.Equal(AccountConfirmationService.ErrorMessageForWrongPasswordOnConfirmation, await ReadErrorMessageOfThisFieldAsync(answerWithTheWrongVisitorPassword, "Password"));
        Assert.False(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers[0].IsEmailConfirmed);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerWithTheWrongVisitorPassword);

        var answerWithTheRightVisitorPassword = await SendAccountConfirmationRequestToTheApiAsync(clientThatCallsTheApiInMemory, tokenOfTheEmailConfirmationLink, ValidPasswordChosenInTheseTests);
        Assert.Equal(HttpStatusCode.OK, answerWithTheRightVisitorPassword.StatusCode);
    }

    // Lien inconnu, déjà utilisé ou expiré : le visiteur reçoit le même message, sur le champ du lien.
    [Fact]
    public async Task UnknownLinkIsRefusedOnTheFieldOfTheLink()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        var answerWithTheUnknownConfirmationLink = await SendAccountConfirmationRequestToTheApiAsync(clientThatCallsTheApiInMemory, "jeton-inconnu", ValidPasswordChosenInTheseTests);

        Assert.Equal(HttpStatusCode.BadRequest, answerWithTheUnknownConfirmationLink.StatusCode);
        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, await ReadErrorMessageOfThisFieldAsync(answerWithTheUnknownConfirmationLink, "Token"));
    }

    [Fact]
    public async Task LinkUsedTwiceIsRefusedTheSecondTime()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();
        var tokenOfTheEmailConfirmationLink = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatCallsTheApiInMemory, ValidPasswordChosenInTheseTests);
        await SendAccountConfirmationRequestToTheApiAsync(clientThatCallsTheApiInMemory, tokenOfTheEmailConfirmationLink, ValidPasswordChosenInTheseTests);

        var answerToTheSecondConfirmationRequest = await SendAccountConfirmationRequestToTheApiAsync(clientThatCallsTheApiInMemory, tokenOfTheEmailConfirmationLink, ValidPasswordChosenInTheseTests);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheSecondConfirmationRequest.StatusCode);
        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, await ReadErrorMessageOfThisFieldAsync(answerToTheSecondConfirmationRequest, "Token"));
    }

    [Fact]
    public async Task LinkIsRefusedAfterItsTwentyFourHours()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();
        var tokenOfTheEmailConfirmationLink = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatCallsTheApiInMemory, ValidPasswordChosenInTheseTests);
        apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromHours(24));

        var answerWithTheExpiredConfirmationLink = await SendAccountConfirmationRequestToTheApiAsync(clientThatCallsTheApiInMemory, tokenOfTheEmailConfirmationLink, ValidPasswordChosenInTheseTests);

        Assert.Equal(HttpStatusCode.BadRequest, answerWithTheExpiredConfirmationLink.StatusCode);
        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, await ReadErrorMessageOfThisFieldAsync(answerWithTheExpiredConfirmationLink, "Token"));
        Assert.False(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers[0].IsEmailConfirmed);
    }

    // Un champ absent, null ou vide reçoit un message clair en français, jamais une erreur 500.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"token\":null,\"password\":null}")]
    [InlineData("{\"token\":\"\",\"password\":\"\"}")]
    public async Task RequestWithoutTokenAndPasswordIsRefusedWithBothErrors(string bodyOfTheRequestWithoutAnyUsableField)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        var answerToTheRequestWithoutAnyField = await clientThatCallsTheApiInMemory.PostAsync(
            AddressOfTheAccountConfirmationEndpoint,
            new StringContent(bodyOfTheRequestWithoutAnyUsableField, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestWithoutAnyField.StatusCode);
        Assert.Equal("Le lien de confirmation est obligatoire.", await ReadErrorMessageOfThisFieldAsync(answerToTheRequestWithoutAnyField, "Token"));
        Assert.Equal("Le mot de passe est obligatoire.", await ReadErrorMessageOfThisFieldAsync(answerToTheRequestWithoutAnyField, "Password"));
    }

    // SS §4 : seuls les champs prévus sont lus. Un champ ajouté ne confirme rien.
    [Fact]
    public async Task ExtraFieldsSentByTheVisitorCannotConfirmTheAccount()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();
        await RegisterVisitorThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatCallsTheApiInMemory, ValidPasswordChosenInTheseTests);

        var answerToTheRequestWithExtraFields = await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountConfirmationEndpoint,
            new { token = "jeton-inconnu", password = ValidPasswordChosenInTheseTests, isConfirmed = true, isEmailConfirmed = true });

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestWithExtraFields.StatusCode);
        Assert.False(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers[0].IsEmailConfirmed);
    }

    // SS §6 : 10 essais par heure et par adresse IP, puis « trop de requêtes » (429).
    [Fact]
    public async Task EleventhAttemptFromTheSameAddressIsRefusedWithTooManyRequests()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        for (var numberOfTheAttemptBeingSent = 1; numberOfTheAttemptBeingSent <= 10; numberOfTheAttemptBeingSent++)
        {
            var answerToTheAllowedConfirmationAttempt = await SendAccountConfirmationRequestToTheApiAsync(clientThatCallsTheApiInMemory, "jeton-inconnu", ValidPasswordChosenInTheseTests);
            Assert.Equal(HttpStatusCode.BadRequest, answerToTheAllowedConfirmationAttempt.StatusCode);
        }

        var answerToTheEleventhConfirmationAttempt = await SendAccountConfirmationRequestToTheApiAsync(clientThatCallsTheApiInMemory, "jeton-inconnu", ValidPasswordChosenInTheseTests);

        Assert.Equal(HttpStatusCode.TooManyRequests, answerToTheEleventhConfirmationAttempt.StatusCode);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheEleventhConfirmationAttempt);
    }

    // SS §6 : fige la limite écrite dans la stratégie. Le test ne peut pas attendre une heure pour la vérifier.
    [Fact]
    public void ConfirmationLimitFollowsTheValuesOfTheSecurityStrategy()
    {
        var optionsOfTheConfirmationAttemptsLimit = RateLimitingConfiguration.CreateOptionsOfTheConfirmationAttemptsLimit();

        Assert.Equal(10, optionsOfTheConfirmationAttemptsLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromHours(1), optionsOfTheConfirmationAttemptsLimit.Window);
    }

    // La limite de la confirmation ne consomme pas celle de l'inscription, et inversement.
    [Fact]
    public async Task ConfirmationAttemptsDoNotConsumeTheSignupLimit()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatCallsTheApiInMemory = apiStartedInMemoryForThisTest.CreateClient();

        for (var numberOfTheAttemptBeingSent = 1; numberOfTheAttemptBeingSent <= 11; numberOfTheAttemptBeingSent++)
        {
            await SendAccountConfirmationRequestToTheApiAsync(clientThatCallsTheApiInMemory, "jeton-inconnu", ValidPasswordChosenInTheseTests);
        }

        var answerToTheVisitorRegistrationRequest = await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new { email = EmailAddressRegisteredInTheseTests, password = ValidPasswordChosenInTheseTests });

        Assert.Equal(HttpStatusCode.OK, answerToTheVisitorRegistrationRequest.StatusCode);
    }
}
