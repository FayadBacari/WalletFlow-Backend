using WalletFlow.Api.Contracts;
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
    private static HttpClient CreateClientFromIp(ApiFactoryWithFakes api, string ipAddress)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, ipAddress);

        return client;
    }

    // La réponse est la même dans les trois états de l'adresse : seul l'e-mail change (SS §2).
    [Fact]
    public async Task AnswerIsIdenticalWhetherTheAddressIsFreeUnconfirmedOrConfirmed()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        var request = new { email = "visiteur@exemple.test", password = ValidPassword };

        var freeAddressResponse = await client.PostAsJsonAsync(RegistrationEndpoint, request);
        var unconfirmedResponse = await client.PostAsJsonAsync(RegistrationEndpoint, request);
        api.AccountRepository.SavedUsers[0].ConfirmEmail();
        var confirmedResponse = await client.PostAsJsonAsync(RegistrationEndpoint, request);

        Assert.Equal(HttpStatusCode.OK, freeAddressResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unconfirmedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirmedResponse.StatusCode);
        var responseText = await freeAddressResponse.Content.ReadAsStringAsync();
        Assert.Equal(responseText, await unconfirmedResponse.Content.ReadAsStringAsync());
        Assert.Equal(responseText, await confirmedResponse.Content.ReadAsStringAsync());

        // Seul l'e-mail change : un lien de confirmation, un nouveau lien, puis un simple avertissement.
        Assert.Single(api.AccountRepository.SavedUsers);
        var emails = api.EmailSender.SentEmails;
        Assert.Equal(3, emails.Count);
        Assert.Contains("confirm-email#token=", emails[0].Body);
        Assert.Contains("confirm-email#token=", emails[1].Body);
        Assert.DoesNotContain("confirm-email", emails[2].Body);
    }

    // SS §6 : 3 inscriptions par heure pour une même adresse e-mail, même depuis plusieurs adresses IP.
    [Fact]
    public async Task FourthRegistrationOfTheSameAddressIsRefusedEvenFromAnotherIpAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var request = new { email = "victime@exemple.test", password = ValidPassword };

        for (var registrationNumber = 1; registrationNumber <= 3; registrationNumber++)
        {
            var allowedClient = CreateClientFromIp(api, "203.0.113." + registrationNumber);
            var allowedResponse = await allowedClient.PostAsJsonAsync(RegistrationEndpoint, request);
            Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        }

        var fourthClient = CreateClientFromIp(api, "203.0.113.4");
        var fourthResponse = await fourthClient.PostAsJsonAsync(RegistrationEndpoint, request);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourthResponse.StatusCode);
        Assert.Equal(3, api.EmailSender.SentEmails.Count);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(fourthResponse);

        // Une heure plus tard, l'adresse est de nouveau acceptée.
        api.Clock.Advance(TimeSpan.FromHours(1));
        var laterClient = CreateClientFromIp(api, "203.0.113.5");
        var laterResponse = await laterClient.PostAsJsonAsync(RegistrationEndpoint, request);
        Assert.Equal(HttpStatusCode.OK, laterResponse.StatusCode);
    }

    // GR Auth : si l'e-mail ne part pas, le visiteur est invité à réessayer, sans aucun détail technique.
    [Fact]
    public async Task EmailFailureGivesClearErrorAndTheNextRegistrationSendsNewLink()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = productionApi.CreateClient();
        var request = new { email = "visiteur@exemple.test", password = ValidPassword };
        productionApi.EmailSender.AlwaysFails = true;

        var failureResponse = await client.PostAsJsonAsync(RegistrationEndpoint, request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failureResponse.StatusCode);
        var responseText = await failureResponse.Content.ReadAsStringAsync();
        var jsonDocument = JsonDocument.Parse(responseText);
        Assert.Equal(AccountRegistrationController.EmailNotSentMessage, jsonDocument.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("panne simulée", responseText);
        Assert.DoesNotContain("visiteur@exemple.test", responseText);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(failureResponse);

        // Le journal désigne l'utilisateur par son identifiant, jamais par son adresse (SS §7).
        var user = Assert.Single(productionApi.AccountRepository.SavedUsers);
        var logText = string.Join("\n", productionApi.LoggerProvider.Messages);
        Assert.Contains(user.Id.ToString(), logText);

        productionApi.EmailSender.AlwaysFails = false;
        var laterResponse = await client.PostAsJsonAsync(RegistrationEndpoint, request);

        Assert.Equal(HttpStatusCode.OK, laterResponse.StatusCode);
        Assert.Contains("confirm-email#token=", Assert.Single(productionApi.EmailSender.SentEmails).Body);
    }

    // SS §7 : même au niveau le plus détaillé, le journal ne contient ni adresse, ni mot de passe, ni jeton.
    [Fact]
    public async Task TechnicalLogNeverContainsAddressPasswordOrToken()
    {
        using var api = new ApiFactoryLoggingEverything();
        var client = api.CreateClient();
        var request = new { email = "visiteur-du-journal@exemple.test", password = ValidPassword };

        await client.PostAsJsonAsync(RegistrationEndpoint, request);
        var confirmationToken = ReadConfirmationToken(api.EmailSender.SentEmails[0].Body);
        await client.PostAsJsonAsync(ConfirmationEndpoint, new { token = confirmationToken, password = "Autre-Mot-De-Passe-2" });
        await client.PostAsJsonAsync(ConfirmationEndpoint, new { token = confirmationToken, password = ValidPassword });
        api.EmailSender.AlwaysFails = true;
        await client.PostAsJsonAsync(RegistrationEndpoint, request);

        var messages = api.LoggerProvider.Messages;
        Assert.Contains(messages, message => message.StartsWith("Debug"));

        var logText = string.Join("\n", messages);
        Assert.DoesNotContain("visiteur-du-journal@exemple.test", logText);
        Assert.DoesNotContain(ValidPassword, logText);
        Assert.DoesNotContain("Autre-Mot-De-Passe-2", logText);
        Assert.DoesNotContain(confirmationToken, logText);
    }

    [Fact]
    public async Task PasswordBreakingTheRulesIsRefusedWithTheRuleToRespect()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = "trop-court" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = jsonDocument.RootElement.GetProperty("errors");
        Assert.Equal(ErrorMessages.PasswordBreaksRules, errors.GetProperty("Password")[0].GetString());
        Assert.Empty(api.AccountRepository.SavedUsers);
        Assert.Empty(api.EmailSender.SentEmails);
    }

    // Un champ absent, null ou vide reçoit un message clair en français, jamais une erreur 500.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"email\":null,\"password\":null}")]
    [InlineData("{\"email\":\"\",\"password\":\"\"}")]
    [InlineData("{\"email\":\"   \",\"password\":\"   \"}")]
    public async Task RequestWithoutEmailAndPasswordIsRefusedWithBothErrors(string requestBody)
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        var response = await client.PostAsync(
            RegistrationEndpoint,
            new StringContent(requestBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = jsonDocument.RootElement.GetProperty("errors");
        Assert.Equal("L'adresse e-mail est obligatoire.", errors.GetProperty("Email")[0].GetString());
        Assert.Equal("Le mot de passe est obligatoire.", errors.GetProperty("Password")[0].GetString());
    }

    // SS §4 : seuls les champs prévus sont lus. Ajouter un champ à la requête ne donne aucun droit.
    [Fact]
    public async Task ExtraFieldsSentByTheVisitorCannotChooseRoleOrConfirmation()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new
            {
                email = "visiteur@exemple.test",
                password = ValidPassword,
                role = "Administrator",
                isEmailConfirmed = true
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = Assert.Single(api.AccountRepository.SavedUsers);
        Assert.Equal(UserRole.User, user.Role);
        Assert.False(user.IsEmailConfirmed);
    }

    // SS §6 : 5 inscriptions par heure et par adresse IP, puis « trop de requêtes » (429).
    [Fact]
    public async Task SixthRegistrationFromTheSameAddressIsRefusedWithTooManyRequests()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        for (var registrationNumber = 1; registrationNumber <= 5; registrationNumber++)
        {
            var allowedResponse = await client.PostAsJsonAsync(
                RegistrationEndpoint,
                new { email = "visiteur" + registrationNumber + "@exemple.test", password = ValidPassword });
            Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        }

        var sixthResponse = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur6@exemple.test", password = ValidPassword });

        Assert.Equal(HttpStatusCode.TooManyRequests, sixthResponse.StatusCode);
        Assert.Equal(5, api.AccountRepository.SavedUsers.Count);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(sixthResponse);
    }

    // SS §6 : la limite compte les requêtes de chaque adresse IP séparément.
    [Fact]
    public async Task VisitorFromAnotherAddressIsNotBlockedByTheLimitOfTheFirstOne()
    {
        using var api = new ApiFactoryWithFakes();
        var firstClient = CreateClientFromIp(api, "203.0.113.1");
        var secondClient = CreateClientFromIp(api, "203.0.113.2");

        for (var registrationNumber = 1; registrationNumber <= 5; registrationNumber++)
        {
            await firstClient.PostAsJsonAsync(
                RegistrationEndpoint,
                new { email = "visiteur" + registrationNumber + "@exemple.test", password = ValidPassword });
        }

        var sixthResponse = await firstClient.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur6@exemple.test", password = ValidPassword });
        var firstResponse = await secondClient.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur7@exemple.test", password = ValidPassword });

        Assert.Equal(HttpStatusCode.TooManyRequests, sixthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    }

    // SS §6 : fige la limite écrite dans la stratégie. Le test ne peut pas attendre une heure pour la vérifier.
    [Fact]
    public void SignupLimitFollowsTheValuesOfTheSecurityStrategy()
    {
        var signupLimit = RateLimitingConfiguration.CreateSignupLimit();

        Assert.Equal(5, signupLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromHours(1), signupLimit.Window);
    }

    // La description de l'API est publiée en développement (voir le test du mode production).
    [Fact]
    public async Task OpenApiDescriptionIsPublishedInDevelopment()
    {
        using var api = new ApiFactoryInDevelopment();
        var client = api.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // SS §6 : chaque réponse porte les en-têtes de sécurité, avec les valeurs exactes de la stratégie.
    [Fact]
    public async Task RegistrationAnswerCarriesTheFourSecurityHeaders()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = ValidPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    // SS §6 : une requête refusée reçoit les mêmes en-têtes de sécurité.
    [Fact]
    public async Task RefusedRegistrationAnswerAlsoCarriesTheFourSecurityHeaders()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "visiteur@exemple.test", password = "trop-court" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    // Le caractère NUL, que PostgreSQL ne sait pas enregistrer, est écarté avant d'arriver à la base.
    [Fact]
    public async Task EmailAddressContainingNullCharacterIsRefusedBeforeReachingTheDatabase()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = "\"a\\\u0000\"@exemple.fr", password = ValidPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = jsonDocument.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Email", out _));
        Assert.Empty(api.AccountRepository.SavedUsers);
        Assert.Empty(api.EmailSender.SentEmails);
    }
}
