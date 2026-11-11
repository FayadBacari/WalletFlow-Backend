using WalletFlow.Api.Contracts;
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
    private const string OtherPassword = "Autre-Mot-De-Passe-2";
    private const string RegisteredEmail = "visiteur@exemple.test";

    // Envoie une demande d'inscription et renvoie le jeton du lien reçu par e-mail.
    private static async Task<string> RegisterThenReadTokenAsync(
        ApiFactoryWithFakes api,
        HttpClient client,
        string password)
    {
        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = RegisteredEmail, password = password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ReadConfirmationToken(api.EmailSender.SentEmails.Last().Body);
    }

    // Envoie une demande de confirmation.
    private static async Task<HttpResponseMessage> SendConfirmationRequestAsync(
        HttpClient client,
        string linkToken,
        string password)
    {
        return await client.PostAsJsonAsync(
            ConfirmationEndpoint,
            new { token = linkToken, password = password });
    }

    // Lit le premier message d'erreur d'un champ dans la réponse.
    private static async Task<string?> ReadFieldErrorAsync(HttpResponseMessage response, string fieldName)
    {
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return jsonDocument.RootElement.GetProperty("errors").GetProperty(fieldName)[0].GetString();
    }

    [Fact]
    public async Task RightLinkAndPasswordConfirmTheAccount()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        var confirmationToken = await RegisterThenReadTokenAsync(api, client, ValidPassword);

        var response = await SendConfirmationRequestAsync(client, confirmationToken, ValidPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(AccountConfirmationController.ConfirmedMessage, jsonDocument.RootElement.GetProperty("message").GetString());
        Assert.True(api.AccountRepository.SavedUsers[0].IsEmailConfirmed);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    [Fact]
    public async Task WrongPasswordIsRefusedOnItsFieldAndTheLinkStaysUsable()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        var confirmationToken = await RegisterThenReadTokenAsync(api, client, ValidPassword);

        var wrongPasswordResponse = await SendConfirmationRequestAsync(client, confirmationToken, OtherPassword);

        Assert.Equal(HttpStatusCode.BadRequest, wrongPasswordResponse.StatusCode);
        Assert.Equal(ErrorMessages.WrongConfirmationPassword, await ReadFieldErrorAsync(wrongPasswordResponse, "Password"));
        Assert.False(api.AccountRepository.SavedUsers[0].IsEmailConfirmed);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(wrongPasswordResponse);

        var rightPasswordResponse = await SendConfirmationRequestAsync(client, confirmationToken, ValidPassword);
        Assert.Equal(HttpStatusCode.OK, rightPasswordResponse.StatusCode);
    }

    // Lien inconnu, déjà utilisé ou expiré : le visiteur reçoit le même message, sur le champ du lien.
    [Fact]
    public async Task UnknownLinkIsRefusedOnTheFieldOfTheLink()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        var response = await SendConfirmationRequestAsync(client, "jeton-inconnu", ValidPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.ConfirmationLinkNoLongerValid, await ReadFieldErrorAsync(response, "Token"));
    }

    [Fact]
    public async Task LinkUsedTwiceIsRefusedTheSecondTime()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        var confirmationToken = await RegisterThenReadTokenAsync(api, client, ValidPassword);
        await SendConfirmationRequestAsync(client, confirmationToken, ValidPassword);

        var response = await SendConfirmationRequestAsync(client, confirmationToken, ValidPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.ConfirmationLinkNoLongerValid, await ReadFieldErrorAsync(response, "Token"));
    }

    [Fact]
    public async Task LinkIsRefusedAfterItsTwentyFourHours()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        var confirmationToken = await RegisterThenReadTokenAsync(api, client, ValidPassword);
        api.Clock.Advance(TimeSpan.FromHours(24));

        var response = await SendConfirmationRequestAsync(client, confirmationToken, ValidPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.ConfirmationLinkNoLongerValid, await ReadFieldErrorAsync(response, "Token"));
        Assert.False(api.AccountRepository.SavedUsers[0].IsEmailConfirmed);
    }

    // Un champ absent, null ou vide reçoit un message clair en français, jamais une erreur 500.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"token\":null,\"password\":null}")]
    [InlineData("{\"token\":\"\",\"password\":\"\"}")]
    public async Task RequestWithoutTokenAndPasswordIsRefusedWithBothErrors(string requestBody)
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        var response = await client.PostAsync(
            ConfirmationEndpoint,
            new StringContent(requestBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Le lien de confirmation est obligatoire.", await ReadFieldErrorAsync(response, "Token"));
        Assert.Equal("Le mot de passe est obligatoire.", await ReadFieldErrorAsync(response, "Password"));
    }

    // SS §4 : seuls les champs prévus sont lus. Un champ ajouté ne confirme rien.
    [Fact]
    public async Task ExtraFieldsSentByTheVisitorCannotConfirmTheAccount()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        await RegisterThenReadTokenAsync(api, client, ValidPassword);

        var response = await client.PostAsJsonAsync(
            ConfirmationEndpoint,
            new { token = "jeton-inconnu", password = ValidPassword, isConfirmed = true, isEmailConfirmed = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(api.AccountRepository.SavedUsers[0].IsEmailConfirmed);
    }

    // SS §6 : 10 essais par heure et par adresse IP, puis « trop de requêtes » (429).
    [Fact]
    public async Task EleventhAttemptFromTheSameAddressIsRefusedWithTooManyRequests()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        for (var attemptNumber = 1; attemptNumber <= 10; attemptNumber++)
        {
            var allowedResponse = await SendConfirmationRequestAsync(client, "jeton-inconnu", ValidPassword);
            Assert.Equal(HttpStatusCode.BadRequest, allowedResponse.StatusCode);
        }

        var eleventhResponse = await SendConfirmationRequestAsync(client, "jeton-inconnu", ValidPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventhResponse.StatusCode);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(eleventhResponse);
    }

    // SS §6 : fige la limite écrite dans la stratégie. Le test ne peut pas attendre une heure pour la vérifier.
    [Fact]
    public void ConfirmationLimitFollowsTheValuesOfTheSecurityStrategy()
    {
        var confirmationLimit = RateLimitingConfiguration.CreateConfirmationLimit();

        Assert.Equal(10, confirmationLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromHours(1), confirmationLimit.Window);
    }

    // La limite de la confirmation ne consomme pas celle de l'inscription, et inversement.
    [Fact]
    public async Task ConfirmationAttemptsDoNotConsumeTheSignupLimit()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        for (var attemptNumber = 1; attemptNumber <= 11; attemptNumber++)
        {
            await SendConfirmationRequestAsync(client, "jeton-inconnu", ValidPassword);
        }

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = RegisteredEmail, password = ValidPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
