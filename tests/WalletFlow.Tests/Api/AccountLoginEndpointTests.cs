using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la route de connexion de bout en bout, avec le vrai Argon2 et les vrais jetons (GR Auth, SS §2, §3 et §6).
public class AccountLoginEndpointTests
{
    // --- Connexion réussie et cookies

    [Fact]
    public async Task RightPasswordAnswersOkAndWritesTheSessionCookieWithItsExactAttributes()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var response = await SendLoginRequestAsync(browserClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(AccountLoginController.LoggedInMessage, jsonDocument.RootElement.GetProperty("message").GetString());
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);

        var sessionCookie = FindCookie(response, "__Host-walletflow-session");
        Assert.NotNull(sessionCookie);
        Assert.True(sessionCookie.HttpOnly);
        Assert.True(sessionCookie.Secure);
        Assert.Equal(SameSiteMode.Lax, sessionCookie.SameSite);
        Assert.Equal("/", sessionCookie.Path.ToString());
        Assert.Equal(0, sessionCookie.Domain.Length);

        // Sans date de fin, le cookie disparaît à la fermeture du navigateur.
        Assert.Null(sessionCookie.MaxAge);
        Assert.Null(sessionCookie.Expires);
    }

    [Fact]
    public async Task RightPasswordWritesTheDurableDeviceCookieWithItsExactAttributes()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var response = await SendLoginRequestAsync(browserClient);

        var deviceCookie = FindCookie(response, "__Host-walletflow-device");
        Assert.NotNull(deviceCookie);
        Assert.True(deviceCookie.HttpOnly);
        Assert.True(deviceCookie.Secure);
        Assert.Equal(SameSiteMode.Lax, deviceCookie.SameSite);
        Assert.Equal("/", deviceCookie.Path.ToString());
        Assert.Equal(0, deviceCookie.Domain.Length);
        Assert.Equal(TimeSpan.FromDays(365), deviceCookie.MaxAge);
    }

    // Les jetons ne sont visibles dans aucune réponse lisible par un script : seulement dans les cookies HttpOnly.
    [Fact]
    public async Task TokensNeverAppearInTheBodyOfTheAnswer()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var response = await SendLoginRequestAsync(browserClient);

        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FindCookie(response, "__Host-walletflow-session")!.Value.ToString(), responseText);
        Assert.DoesNotContain(FindCookie(response, "__Host-walletflow-device")!.Value.ToString(), responseText);
    }

    // La deuxième connexion du même navigateur garde l'appareil, et renouvelle son cookie.
    [Fact]
    public async Task SecondLoginOfTheSameBrowserKeepsTheDeviceAndRenewsItsCookie()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var firstResponse = await SendLoginRequestAsync(browserClient);
        var secondResponse = await SendLoginRequestAsync(browserClient);

        Assert.Equal(
            FindCookie(firstResponse, "__Host-walletflow-device")!.Value,
            FindCookie(secondResponse, "__Host-walletflow-device")!.Value);
        Assert.Single(api.SessionRepository.SavedDevices);
    }

    // Se reconnecter dans le même navigateur ferme la session d'avant : une seule reste ouverte.
    [Fact]
    public async Task SecondLoginOfTheSameBrowserClosesTheFirstSession()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        await SendLoginRequestAsync(browserClient);
        await SendLoginRequestAsync(browserClient);

        Assert.Single(api.SessionRepository.SavedSessions);
    }

    // --- Refus

    [Theory]
    [InlineData(Email, WrongPassword)] // mauvais mot de passe
    [InlineData("inconnu@exemple.test", ValidPassword)]        // adresse inconnue
    [InlineData("sans-arobase", ValidPassword)]                // adresse mal formée
    public async Task WrongCredentialsAnswerTheExactGenericMessageWithoutAnyCookie(string emailAddress, string password)
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var response = await SendLoginRequestAsync(browserClient, emailAddress, password);

        await AssertProblemTitleAsync(response, HttpStatusCode.Unauthorized, "Identifiants incorrects");
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(api.SessionRepository.SavedSessions);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    // Lit tout ce qui compte dans une réponse d'erreur : son code, son titre et sa forme.
    private static async Task<string> DescribeErrorWithoutTraceIdAsync(HttpResponseMessage response)
    {
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var description = (int)response.StatusCode + "|";

        foreach (var property in jsonDocument.RootElement.EnumerateObject())
        {
            if (property.Name != "traceId")
            {
                description = description + property.Name + "=" + property.Value + "|";
            }
        }

        return description;
    }

    // Une adresse inconnue, un mauvais mot de passe et un compte bloqué doivent donner exactement la même réponse.
    [Fact]
    public async Task UnknownAddressWrongPasswordAndLockedAccountGiveExactlyTheSameAnswer()
    {
        using var api = new ApiFactoryWithFakes();
        await AddLockedAccountAsync(api, "compte-bloque@exemple.test", 1, api.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        await AddAccountAsync(api, "compte-ouvert@exemple.test");
        var browserClient = CreateBrowserClient(api);

        var unknownAddressResponse = await SendLoginRequestAsync(browserClient, "inconnu@exemple.test", ValidPassword);
        var wrongPasswordResponse = await SendLoginRequestAsync(browserClient, "compte-ouvert@exemple.test", WrongPassword);
        var lockedAccountResponse = await SendLoginRequestAsync(browserClient, "compte-bloque@exemple.test", ValidPassword);

        var unknownAddressDescription = await DescribeErrorWithoutTraceIdAsync(unknownAddressResponse);
        Assert.Equal(unknownAddressDescription, await DescribeErrorWithoutTraceIdAsync(wrongPasswordResponse));
        Assert.Equal(unknownAddressDescription, await DescribeErrorWithoutTraceIdAsync(lockedAccountResponse));
    }

    [Fact]
    public async Task UnconfirmedAccountWithRightPasswordAnswersForbiddenWithTheInvitation()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api, isEmailConfirmed: false);
        var browserClient = CreateBrowserClient(api);

        var response = await SendLoginRequestAsync(browserClient);

        await AssertProblemTitleAsync(
            response,
            HttpStatusCode.Forbidden,
            "Ton adresse e-mail n'est pas encore confirmée. Ouvre le lien reçu par e-mail, ou inscris-toi à nouveau pour en recevoir un autre.");
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task UnconfirmedAccountWithWrongPasswordGetsTheGenericMessage()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api, isEmailConfirmed: false);
        var browserClient = CreateBrowserClient(api);

        var response = await SendLoginRequestAsync(browserClient, Email, WrongPassword);

        await AssertProblemTitleAsync(response, HttpStatusCode.Unauthorized, "Identifiants incorrects");
    }

    [Fact]
    public async Task FiveWrongPasswordsLockTheAccountAndTheRightPasswordIsThenRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            await SendLoginRequestAsync(browserClient, Email, WrongPassword);
        }

        var lockedResponse = await SendLoginRequestAsync(browserClient);

        await AssertProblemTitleAsync(lockedResponse, HttpStatusCode.Unauthorized, "Identifiants incorrects");
        Assert.Equal(1, account.LockoutCount);

        api.Clock.Advance(TimeSpan.FromMinutes(15));
        var unlockedResponse = await SendLoginRequestAsync(browserClient);

        Assert.Equal(HttpStatusCode.OK, unlockedResponse.StatusCode);
    }

    // SS §11 : le journal note l'adresse IP de la requête.
    [Fact]
    public async Task IpAddressOfTheRequestIsWrittenInTheSecurityJournal()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        browserClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.7");

        await SendLoginRequestAsync(browserClient, Email, WrongPassword);

        var securityEvent = Assert.Single(api.SecurityEventRepository.SavedSecurityEvents);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEvent.IpAddress);
    }

    // --- Limite de débit

    [Fact]
    public async Task LoginIsLimitedToTenAttemptsPerMinuteAndPerIpAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var firstClient = CreateBrowserClient(api);
        firstClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.10");
        var secondClient = CreateBrowserClient(api);
        secondClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.11");

        for (var attemptNumber = 1; attemptNumber <= 10; attemptNumber++)
        {
            var allowedResponse = await SendLoginRequestAsync(firstClient, "inconnu@exemple.test");
            Assert.Equal(HttpStatusCode.Unauthorized, allowedResponse.StatusCode);
        }

        var overLimitResponse = await SendLoginRequestAsync(firstClient, "inconnu@exemple.test");
        await AssertProblemTitleAsync(overLimitResponse, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");

        var otherAddressResponse = await SendLoginRequestAsync(secondClient, "inconnu@exemple.test");
        Assert.Equal(HttpStatusCode.Unauthorized, otherAddressResponse.StatusCode);
    }

    // Le test ne peut pas attendre : il vérifie la durée exacte de la limite (SS §6).
    [Fact]
    public void LoginLimitLastsExactlyOneMinute()
    {
        var loginLimit = RateLimitingConfiguration.CreateLoginLimit();

        Assert.Equal(10, loginLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), loginLimit.Window);
    }

    // --- Entrées hostiles

    // Envoie un texte JSON écrit à la main sur la route de connexion.
    private static async Task<HttpResponseMessage> SendRawLoginJsonAsync(HttpClient client, string rawJson)
    {
        return await client.PostAsync(LoginEndpoint, new StringContent(rawJson, Encoding.UTF8, "application/json"));
    }

    // Aucune de ces valeurs ne doit provoquer d'erreur 500 : le visiteur reçoit le refus normal.
    [Theory]
    [InlineData("""{"email":"utilisateur@exemple.test","password":"Abc￾"}""")]     // caractère que .NET refuse de normaliser
    [InlineData("""{"email":"utilisateur@exemple.test","password":"Abc￿"}""")]     // caractère interdit par Unicode
    [InlineData("""{"email":"utilisateur@exemple.test","password":"Abc\u0000def"}""")]  // caractère nul
    [InlineData("""{"email":"nul\u0000@exemple.test","password":"Mot-De-Passe-Valide-1"}""")] // caractère nul, refusé par PostgreSQL
    [InlineData("""{"email":"utilisateur@exemple.test\n","password":"Abc-Mauvais-1"}""")]     // retour à la ligne
    [InlineData("""{"email":"' OR 1=1 --@exemple.test","password":"' OR 1=1 --"}""")]         // injection SQL
    public async Task HostileValuesGetTheNormalRefusal(string rawJson)
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var response = await SendRawLoginJsonAsync(browserClient, rawJson);

        await AssertProblemTitleAsync(response, HttpStatusCode.Unauthorized, "Identifiants incorrects");
    }

    // Le lecteur JSON de .NET refuse une moitié d'emoji : elle n'arrive donc jamais jusqu'aux règles du mot de passe.
    [Theory]
    [InlineData("""{"email":"utilisateur@exemple.test","password":"Abc\ud800"}""")]
    [InlineData("""{"email":"utilisateur@exemple.test","password":"Abc\udc00"}""")]
    [InlineData("""{"email":"utilisateur\ud800@exemple.test","password":"Abc"}""")]
    public async Task HalfOfAnEmojiIsRefusedAsUnreadableRequest(string rawJson)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await SendRawLoginJsonAsync(browserClient, rawJson);

        await AssertProblemTitleAsync(response, HttpStatusCode.BadRequest, ApiErrorConfiguration.RefusedRequestTitle);
        Assert.DoesNotContain("ud800", await response.Content.ReadAsStringAsync());
    }

    // Les champs manquants donnent des messages en français, sans nom interne du code.
    [Theory]
    [InlineData("""{"password":"Mot-De-Passe-Valide-1"}""", "Email", "L'adresse e-mail est obligatoire.")]
    [InlineData("""{"email":"utilisateur@exemple.test"}""", "Password", "Le mot de passe est obligatoire.")]
    [InlineData("""{"email":null,"password":"Mot-De-Passe-Valide-1"}""", "Email", "L'adresse e-mail est obligatoire.")]
    [InlineData("""{"email":"utilisateur@exemple.test","password":""}""", "Password", "Le mot de passe est obligatoire.")]
    public async Task MissingFieldIsRefusedWithItsFrenchMessage(string rawJson, string fieldName, string message)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await SendRawLoginJsonAsync(browserClient, rawJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(message, jsonDocument.RootElement.GetProperty("errors").GetProperty(fieldName)[0].GetString());
        Assert.Empty(api.SecurityEventRepository.SavedSecurityEvents);
    }

    [Theory]
    [InlineData("pas du JSON")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("""{"email":12,"password":34}""")]
    public async Task UnreadableBodyIsRefusedWithoutAnyInternalName(string rawJson)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await SendRawLoginJsonAsync(browserClient, rawJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(nameof(LoginRequest), responseText);
        Assert.DoesNotContain("requestSentByTheVisitor", responseText);
    }

    // Un site piégé ne peut envoyer qu'un formulaire ou du texte sans demander d'autorisation : ils sont refusés (SS §6).
    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task ContentTypesThatATrappedWebsiteCanSendAreRefused(string contentType)
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var body = new StringContent("""{"email":"utilisateur@exemple.test","password":"Mot-De-Passe-Valide-1"}""", Encoding.UTF8, contentType);

        var response = await browserClient.PostAsync(LoginEndpoint, body);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(api.SessionRepository.SavedSessions);
    }

    // Le troisième type de formulaire qu'un site piégé peut envoyer sans demander d'autorisation.
    [Fact]
    public async Task MultipartFormOfATrappedWebsiteIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var form = new MultipartFormDataContent
        {
            { new StringContent(Email), "email" },
            { new StringContent(ValidPassword), "password" }
        };

        var response = await browserClient.PostAsync(LoginEndpoint, form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(api.SessionRepository.SavedSessions);
    }

    // Seuls l'adresse et le mot de passe sont lus : un champ ajouté ne change rien (SS §4).
    [Fact]
    public async Task ExtraFieldsInTheRequestAreIgnored()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var response = await browserClient.PostAsJsonAsync(LoginEndpoint, new
        {
            email = Email,
            password = ValidPassword,
            role = "administrateur",
            userId = Guid.NewGuid(),
            failedLoginAttemptCount = 0
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(UserRole.User, account.Role);
        Assert.Equal(account.Id, Assert.Single(api.SessionRepository.SavedSessions).UserId);
    }

    // Un cookie de session invalide ne doit pas empêcher de se connecter.
    [Fact]
    public async Task JunkSessionCookieDoesNotPreventTheLogin()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateClientWithManualCookies(api, "__Host-walletflow-session=faux; __Host-walletflow-device=faux");

        var response = await SendLoginRequestAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("faux", FindCookie(response, "__Host-walletflow-device")!.Value.ToString());
    }

    // --- Journal technique

    // Au niveau le plus détaillé, le journal technique ne contient jamais de secret (SS §7).
    [Fact]
    public async Task TechnicalLogNeverContainsTheSecretsOfTheLogin()
    {
        using var api = new ApiFactoryLoggingEverything();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        await SendLoginRequestAsync(browserClient, Email, WrongPassword);
        var response = await SendLoginRequestAsync(browserClient);
        var csrfToken = await GetCsrfTokenAsync(browserClient);
        await SendLogoutRequestAsync(browserClient, csrfToken);

        var logText = string.Join("\n", api.LoggerProvider.Messages);
        Assert.NotEmpty(logText);
        Assert.DoesNotContain(Email, logText);
        Assert.DoesNotContain(ValidPassword, logText);
        Assert.DoesNotContain(WrongPassword, logText);
        Assert.DoesNotContain(FindCookie(response, "__Host-walletflow-session")!.Value.ToString(), logText);
        Assert.DoesNotContain(FindCookie(response, "__Host-walletflow-device")!.Value.ToString(), logText);
        Assert.DoesNotContain(csrfToken, logText);
    }
}
