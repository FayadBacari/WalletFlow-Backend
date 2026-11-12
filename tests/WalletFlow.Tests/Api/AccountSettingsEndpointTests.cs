using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes du compte et des données personnelles (GR Compte, SS §2, §3 et §6) dans la vraie API, avec une fausse base.
public class AccountSettingsEndpointTests
{
    private const string ProfileEndpoint = "/api/account/profile";
    private const string NameEndpoint = "/api/account/name";
    private const string PasswordEndpoint = "/api/account/password";
    private const string EmailEndpoint = "/api/account/email";
    private const string EmailChangeConfirmationEndpoint = "/api/account/confirm-email-change";
    private const string ChangedPassword = "Nouveau-Mot-De-Passe-3";
    private const string NewEmail = "nouvelle@exemple.test";

    // Lit le profil avec ce client.
    private static async Task<JsonElement> ReadProfileAsync(HttpClient client)
    {
        var response = await client.GetAsync(ProfileEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Demande le changement de mot de passe, avec le mot de passe des tests et ce nouveau mot de passe.
    private static async Task<HttpResponseMessage> SendPasswordChangeAsync(HttpClient client, string csrfToken, string currentPassword, string? code = null)
    {
        return await SendSettingsRequestAsync(
            client,
            PasswordEndpoint,
            new { currentPassword = currentPassword, newPassword = ChangedPassword, code = code },
            csrfToken);
    }

    // Demande une nouvelle adresse, avec le mot de passe des tests.
    private static async Task<HttpResponseMessage> SendEmailChangeAsync(HttpClient client, string csrfToken, string newEmail)
    {
        return await SendSettingsRequestAsync(
            client,
            EmailEndpoint,
            new { password = ValidPassword, newEmail = newEmail },
            csrfToken);
    }

    // Attend l'e-mail parti vers cette adresse, et le renvoie.
    private static async Task<WalletFlow.Application.Emails.EmailMessage> WaitForEmailToAsync(ApiFactoryWithFakes api, string recipient)
    {
        await WaitUntilAsync(() => api.EmailSender.CountSentEmailsTo(recipient) == 1);

        return api.EmailSender.SentEmails.Single(email => email.RecipientAddress == recipient);
    }

    // --- Profil

    [Fact]
    public async Task ProfileNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();

        var response = await api.CreateClient().GetAsync(ProfileEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Ce sont des données personnelles : aucun navigateur ni intermédiaire ne doit les garder.
    [Fact]
    public async Task ProfileGivesTheDataOfTheConnectedAccountAndIsNeverCached()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        await LogInThenGetCsrfTokenAsync(client);

        var response = await client.GetAsync(ProfileEndpoint);
        var profile = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Email, profile.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("lastName").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("firstName").ValueKind);
        Assert.False(profile.GetProperty("isTwoFactorEnabled").GetBoolean());
        Assert.Equal(4, profile.EnumerateObject().Count());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    // --- Nom et prénom

    [Fact]
    public async Task NameChangeNeedsTheCsrfToken()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        await LogInThenGetCsrfTokenAsync(client);

        var response = await SendSettingsRequestAsync(client, NameEndpoint, new { lastName = "Dupont", firstName = "Élise" }, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(account.LastName);
    }

    [Fact]
    public async Task NameIsSavedThenShownInTheProfileThenErased()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        var savedResponse = await SendSettingsRequestAsync(client, NameEndpoint, new { lastName = " Dupont ", firstName = "Élise" }, csrfToken);
        var savedProfile = await ReadProfileAsync(client);
        var erasedResponse = await SendSettingsRequestAsync(client, NameEndpoint, new { lastName = "", firstName = (string?)null }, csrfToken);
        var erasedProfile = await ReadProfileAsync(client);

        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        Assert.Equal("Dupont", savedProfile.GetProperty("lastName").GetString());
        Assert.Equal("Élise", savedProfile.GetProperty("firstName").GetString());
        Assert.Equal(HttpStatusCode.OK, erasedResponse.StatusCode);
        Assert.Equal(JsonValueKind.Null, erasedProfile.GetProperty("lastName").ValueKind);
        Assert.Equal(JsonValueKind.Null, erasedProfile.GetProperty("firstName").ValueKind);
    }

    // Chaque champ refusé garde sa propre clé, avec le message des règles.
    [Fact]
    public async Task InvalidNameFieldsAreRefusedOnTheirOwnKeys()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);
        var firstNameWithInvisibleCharacter = "Éli" + (char)0x200B + "se";

        var response = await SendSettingsRequestAsync(client, NameEndpoint, new { lastName = new string('a', 51), firstName = firstNameWithInvisibleCharacter }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.PersonalNameBreaksRules, await ReadFieldErrorAsync(response, "LastName"));
        Assert.Equal(ErrorMessages.PersonalNameBreaksRules, await ReadFieldErrorAsync(response, "FirstName"));
        Assert.Null(account.LastName);
    }

    // --- Mot de passe

    [Fact]
    public async Task PasswordChangeNeedsASessionAndTheCsrfToken()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var oldPasswordHash = account.PasswordHash;
        var client = CreateBrowserClient(api);

        var withoutSessionResponse = await SendPasswordChangeAsync(client, "jeton-anti-csrf-invente", ValidPassword);
        await LogInThenGetCsrfTokenAsync(client);
        var withoutTokenResponse = await SendSettingsRequestAsync(client, PasswordEndpoint, new { currentPassword = ValidPassword, newPassword = ChangedPassword }, null);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutSessionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutTokenResponse.StatusCode);
        Assert.Equal(oldPasswordHash, account.PasswordHash);
    }

    // SS §3 : la session en cours reçoit un nouvel identifiant, l'ancien cookie ne vaut plus rien, et les autres appareils sont déconnectés.
    // Le jeton anti-CSRF reste valable : il est lié à l'utilisateur, pas à l'identifiant de sa session.
    [Fact]
    public async Task PasswordChangeGivesANewSessionCookieAndClosesEveryOtherSession()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var currentBrowser = CreateBrowserClient(api);
        var otherBrowser = CreateBrowserClient(api);
        var loginResponse = await SendLoginRequestAsync(currentBrowser);
        var oldSessionToken = FindCookie(loginResponse, SessionCookiesConfiguration.SessionCookieName)!.Value.ToString();
        var csrfToken = await GetCsrfTokenAsync(currentBrowser);
        await LogInThenGetCsrfTokenAsync(otherBrowser);

        var response = await SendPasswordChangeAsync(currentBrowser, csrfToken, ValidPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newSessionCookie = FindCookie(response, SessionCookiesConfiguration.SessionCookieName);
        Assert.NotNull(newSessionCookie);
        Assert.NotEqual(oldSessionToken, newSessionCookie.Value.ToString());
        Assert.True(newSessionCookie.HttpOnly);
        Assert.True(newSessionCookie.Secure);
        await ReadProfileAsync(currentBrowser);
        var nameResponse = await SendSettingsRequestAsync(currentBrowser, NameEndpoint, new { lastName = "Dupont" }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, nameResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherBrowser.GetAsync(ProfileEndpoint)).StatusCode);
        var stolenCookieClient = CreateClientWithManualCookies(api, SessionCookiesConfiguration.SessionCookieName + "=" + oldSessionToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stolenCookieClient.GetAsync(ProfileEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendLoginRequestAsync(api.CreateClient(), Email, ValidPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestAsync(api.CreateClient(), Email, ChangedPassword)).StatusCode);
    }

    [Fact]
    public async Task WrongCurrentPasswordIsRefusedOnItsField()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        var response = await SendPasswordChangeAsync(client, csrfToken, WrongPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.WrongCurrentPassword, await ReadFieldErrorAsync(response, "CurrentPassword"));
        Assert.Null(FindCookie(response, SessionCookiesConfiguration.SessionCookieName));
    }

    [Fact]
    public async Task NewPasswordBreakingTheRulesIsRefusedOnItsField()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        var response = await SendSettingsRequestAsync(client, PasswordEndpoint, new { currentPassword = ValidPassword, newPassword = "trop-court" }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.PasswordBreaksRules, await ReadFieldErrorAsync(response, "NewPassword"));
    }

    // Avec la double authentification, le refus ne dit pas si le mot de passe ou le code est faux.
    [Fact]
    public async Task TwoFactorAccountGetsOneSharedRefusalThenChangesItsPasswordWithTheRightCode()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInWithCodeThenGetCsrfTokenAsync(api, client, activatedAccount.Secret);
        WaitForNextCode(api);

        var wrongCodeResponse = await SendPasswordChangeAsync(client, csrfToken, ValidPassword, FindWrongCode(api, activatedAccount.Secret));
        var rightCodeResponse = await SendPasswordChangeAsync(client, csrfToken, ValidPassword, CalculateCurrentCode(api, activatedAccount.Secret));

        Assert.Equal(HttpStatusCode.BadRequest, wrongCodeResponse.StatusCode);
        Assert.Equal(ErrorMessages.WrongPasswordOrCode, await ReadFieldErrorAsync(wrongCodeResponse, ApiErrorConfiguration.GeneralErrorsKey));
        Assert.Equal(HttpStatusCode.OK, rightCodeResponse.StatusCode);
    }

    // SS §6 : 10 changements de mot de passe par heure et par adresse IP.
    [Fact]
    public async Task PasswordChangeIsLimitedToTenPerHourAndPerIpAddress()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        for (var attemptNumber = 1; attemptNumber <= 10; attemptNumber++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SendPasswordChangeAsync(client, csrfToken, WrongPassword)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendPasswordChangeAsync(client, csrfToken, ValidPassword)).StatusCode);
    }

    // --- Adresse e-mail

    // SS §2 : la même réponse que la nouvelle adresse soit libre ou prise. Seul l'e-mail préparé ensuite change.
    [Fact]
    public async Task EmailChangeAnswersTheSameForAFreeAndATakenAddressThenSendsTheRightEmail()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        await AddAccountAsync(api, "deja-prise@exemple.test");
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        var freeAddressResponse = await SendEmailChangeAsync(client, csrfToken, NewEmail);
        var takenAddressResponse = await SendEmailChangeAsync(client, csrfToken, "deja-prise@exemple.test");

        Assert.Equal(HttpStatusCode.OK, freeAddressResponse.StatusCode);
        Assert.Equal(await freeAddressResponse.Content.ReadAsStringAsync(), await takenAddressResponse.Content.ReadAsStringAsync());
        Assert.Contains("/confirm-email-change#token=", (await WaitForEmailToAsync(api, NewEmail)).Body);
        Assert.DoesNotContain("#token=", (await WaitForEmailToAsync(api, "deja-prise@exemple.test")).Body);
    }

    // GR Compte : le lien et le mot de passe changent l'adresse, la nouvelle sert à se connecter, et l'ancienne est prévenue.
    [Fact]
    public async Task ConfirmedEmailChangeLetsTheUserLogInWithTheNewAddressAndWarnsTheOldOne()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);
        await SendEmailChangeAsync(client, csrfToken, NewEmail);
        var linkToken = ReadConfirmationToken((await WaitForEmailToAsync(api, NewEmail)).Body);

        var response = await api.CreateClient().PostAsJsonAsync(EmailChangeConfirmationEndpoint, new { token = linkToken, password = ValidPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EmailChangeConfirmationController.EmailChangedMessage, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain(NewEmail, (await WaitForEmailToAsync(api, Email)).Body);
        Assert.Equal(NewEmail, (await ReadProfileAsync(client)).GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendLoginRequestAsync(api.CreateClient(), Email, ValidPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestAsync(api.CreateClient(), NewEmail, ValidPassword)).StatusCode);
    }

    [Fact]
    public async Task EmailChangeConfirmationRefusesAWrongPasswordAndAnInvalidLinkOnTheirFields()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);
        await SendEmailChangeAsync(client, csrfToken, NewEmail);
        var linkToken = ReadConfirmationToken((await WaitForEmailToAsync(api, NewEmail)).Body);
        var visitor = api.CreateClient();

        var wrongPasswordResponse = await visitor.PostAsJsonAsync(EmailChangeConfirmationEndpoint, new { token = linkToken, password = WrongPassword });
        var unknownLinkResponse = await visitor.PostAsJsonAsync(EmailChangeConfirmationEndpoint, new { token = "jeton-inconnu", password = ValidPassword });

        Assert.Equal(ErrorMessages.WrongCurrentPassword, await ReadFieldErrorAsync(wrongPasswordResponse, "Password"));
        Assert.Equal(ErrorMessages.EmailChangeLinkNoLongerValid, await ReadFieldErrorAsync(unknownLinkResponse, "Token"));
        Assert.Equal(Email, (await ReadProfileAsync(client)).GetProperty("email").GetString());
    }

    // Un autre compte a pris l'adresse depuis la demande : le refus est clair. Seul celui qui a le lien et le mot de passe le lit.
    [Fact]
    public async Task EmailChangeConfirmationOfATakenAddressIsAConflict()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);
        await SendEmailChangeAsync(client, csrfToken, NewEmail);
        var linkToken = ReadConfirmationToken((await WaitForEmailToAsync(api, NewEmail)).Body);
        api.AccountRepository.SavedUsers.Add(new User { Email = NewEmail, PasswordHash = "empreinte-d-un-autre-compte", IsEmailConfirmed = true });

        var response = await api.CreateClient().PostAsJsonAsync(EmailChangeConfirmationEndpoint, new { token = linkToken, password = ValidPassword });

        await AssertProblemTitleAsync(response, HttpStatusCode.Conflict, ErrorMessages.EmailAlreadyUsed);
    }

    // SS §6 : 10 demandes de changement d'adresse par heure et par adresse IP.
    [Fact]
    public async Task EmailChangeIsLimitedToTenPerHourAndPerIpAddress()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SendEmailChangeAsync(client, csrfToken, Email)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendEmailChangeAsync(client, csrfToken, NewEmail)).StatusCode);
    }

    // SS §6 : 10 essais du lien de changement d'adresse par heure et par adresse IP.
    [Fact]
    public async Task EmailChangeLinkIsLimitedToTenAttemptsPerHourAndPerIpAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var visitor = api.CreateClient();

        for (var attemptNumber = 1; attemptNumber <= 10; attemptNumber++)
        {
            var response = await visitor.PostAsJsonAsync(EmailChangeConfirmationEndpoint, new { token = "jeton-inconnu", password = ValidPassword });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var limitedResponse = await visitor.PostAsJsonAsync(EmailChangeConfirmationEndpoint, new { token = "jeton-inconnu", password = ValidPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
    }

    // --- Inventaire

    // Toute route qui reçoit un corps déclare la limite de taille : une nouvelle route ne peut pas l'oublier.
    [Fact]
    public void EveryPostRouteDeclaresTheSizeLimit()
    {
        using var api = new ApiFactoryWithFakes();
        var routeProvider = api.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var routesWithoutLimit = new List<string>();

        foreach (var route in routeProvider.ActionDescriptors.Items)
        {
            var httpMethods = route.EndpointMetadata.OfType<IHttpMethodMetadata>().SelectMany(metadata => metadata.HttpMethods);
            var sizeLimit = route.EndpointMetadata.OfType<IRequestSizeLimitMetadata>().LastOrDefault();

            if (httpMethods.Contains(HttpMethods.Post)
                && sizeLimit?.MaxRequestBodySize != RequestSizeConfiguration.MaximumAccountRequestBytes)
            {
                routesWithoutLimit.Add(route.AttributeRouteInfo!.Template!);
            }
        }

        Assert.Empty(routesWithoutLimit);
    }
}
