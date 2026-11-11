using WalletFlow.Api.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Controllers;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la connexion avec un code, de bout en bout, avec le vrai Argon2 et les vrais calculs (GR Auth, SS §2, §3 et §6).
public class TwoFactorLoginEndpointTests
{
    // --- Le mot de passe seul

    [Fact]
    public async Task RightPasswordOfAnActivatedAccountAsksForTheCodeAndOpensNoSession()
    {
        using var api = new ApiFactoryWithFakes();
        await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var loginResponse = await SendLoginRequestAsync(browserClient);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var jsonDocument = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        Assert.True(jsonDocument.RootElement.GetProperty("isTwoFactorCodeRequired").GetBoolean());
        Assert.Equal(AccountLoginController.CodeRequiredMessage, jsonDocument.RootElement.GetProperty("message").GetString());
        Assert.Null(FindCookie(loginResponse, SessionCookiesConfiguration.SessionCookieName));
        Assert.Null(FindCookie(loginResponse, SessionCookiesConfiguration.DeviceCookieName));
        Assert.Empty(api.SessionRepository.SavedSessions);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(loginResponse);
    }

    // SS §3 : ce cookie n'est lisible par aucun script, ne part qu'en HTTPS, et ne vaut que pour le site lui-même.
    [Fact]
    public async Task WaitingLoginCookieHasItsExactAttributesAndNeverAppearsInTheBody()
    {
        using var api = new ApiFactoryWithFakes();
        await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var loginResponse = await SendLoginRequestAsync(browserClient);

        var waitingLoginCookie = FindCookie(loginResponse, WaitingLoginCookieName);
        Assert.NotNull(waitingLoginCookie);
        Assert.True(waitingLoginCookie.HttpOnly);
        Assert.True(waitingLoginCookie.Secure);
        Assert.Equal(SameSiteMode.Strict, waitingLoginCookie.SameSite);
        Assert.Equal("/", waitingLoginCookie.Path.ToString());
        Assert.Equal(0, waitingLoginCookie.Domain.Length);
        Assert.Null(waitingLoginCookie.MaxAge);
        Assert.Null(waitingLoginCookie.Expires);
        Assert.DoesNotContain(waitingLoginCookie.Value.ToString(), await loginResponse.Content.ReadAsStringAsync());
    }

    // Le contrat avec le site : une connexion sans double authentification ne demande aucun code.
    [Fact]
    public async Task AccountWithoutTwoFactorIsToldThatNoCodeIsRequired()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var loginResponse = await SendLoginRequestAsync(browserClient);

        var jsonDocument = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        Assert.False(jsonDocument.RootElement.GetProperty("isTwoFactorCodeRequired").GetBoolean());
        Assert.Null(FindCookie(loginResponse, WaitingLoginCookieName));
    }

    // Le cookie d'attente n'ouvre aucune porte : il ne vaut pas une session.
    [Fact]
    public async Task WaitingLoginCookieAloneGivesAccessToNothing()
    {
        using var api = new ApiFactoryWithFakes();
        await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        var protectedResponse = await browserClient.GetAsync(CsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
    }

    // Copié dans le cookie de session, le jeton d'attente n'est reconnu par aucune session.
    [Fact]
    public async Task WaitingLoginTokenCopiedIntoTheSessionCookieGivesAccessToNothing()
    {
        using var api = new ApiFactoryWithFakes();
        await AddActivatedAccountAsync(api);
        var loginResponse = await SendLoginRequestAsync(CreateBrowserClient(api));
        var waitingLoginToken = FindCookie(loginResponse, WaitingLoginCookieName)!.Value.ToString();
        var attackerClient = CreateClientWithManualCookies(api, SessionCookiesConfiguration.SessionCookieName + "=" + waitingLoginToken);

        var protectedResponse = await attackerClient.GetAsync(CsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
    }

    // --- Le bon code

    [Fact]
    public async Task RightCodeOpensTheSessionWritesItsCookiesAndDeletesTheWaitingCookie()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        var codeResponse = await SendTwoFactorLoginRequestAsync(browserClient, CalculateCurrentCode(api, activatedAccount.Secret));

        Assert.Equal(HttpStatusCode.OK, codeResponse.StatusCode);
        var jsonDocument = JsonDocument.Parse(await codeResponse.Content.ReadAsStringAsync());
        Assert.False(jsonDocument.RootElement.GetProperty("isTwoFactorCodeRequired").GetBoolean());
        Assert.Equal(AccountLoginController.LoggedInMessage, jsonDocument.RootElement.GetProperty("message").GetString());

        var sessionCookie = FindCookie(codeResponse, SessionCookiesConfiguration.SessionCookieName);
        Assert.NotNull(sessionCookie);
        Assert.True(sessionCookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, sessionCookie.SameSite);
        Assert.Equal(TimeSpan.FromDays(365), FindCookie(codeResponse, SessionCookiesConfiguration.DeviceCookieName)!.MaxAge);

        // Un navigateur préfère Max-Age à une date passée : l'en-tête de suppression ne doit donc pas en contenir.
        var waitingLoginCookie = FindCookie(codeResponse, WaitingLoginCookieName);
        Assert.NotNull(waitingLoginCookie);
        Assert.Null(waitingLoginCookie.MaxAge);
        Assert.True(waitingLoginCookie.Expires < DateTimeOffset.UtcNow);
        Assert.Equal(0, waitingLoginCookie.Value.Length);

        var protectedResponse = await browserClient.GetAsync(CsrfTokenEndpoint);
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task BackupCodeWithItsDashAlsoOpensTheSession()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        var codeResponse = await SendTwoFactorLoginRequestAsync(browserClient, activatedAccount.BackupCode.Insert(5, "-"));

        Assert.Equal(HttpStatusCode.OK, codeResponse.StatusCode);
        Assert.NotNull(api.TwoFactorRepository.SavedBackupCodes[0].UsedAt);
    }

    // Le cookie d'appareil du navigateur est lu au moment du code : un appareil déjà connu du compte n'en crée pas d'autre.
    [Fact]
    public async Task KnownDeviceDoesNotCreateANewDeviceAtTheEndOfTheLogin()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await LogInWithCodeThenGetCsrfTokenAsync(api, browserClient, activatedAccount.Secret);
        WaitForNextCode(api);

        await LogInWithCodeThenGetCsrfTokenAsync(api, browserClient, activatedAccount.Secret);

        Assert.Single(api.SessionRepository.SavedDevices);
        Assert.Single(api.SessionRepository.SavedSessions);
    }

    // SS §2 : dans la vraie API aussi, la mémoire des codes utilisés survit d'une requête à l'autre.
    [Fact]
    public async Task CodeAlreadyUsedIsRefusedAtTheNextLoginThroughTheApi()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var code = CalculateCurrentCode(api, activatedAccount.Secret);
        await SendLoginRequestAsync(browserClient);
        var firstResponse = await SendTwoFactorLoginRequestAsync(browserClient, code);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        await SendLoginRequestAsync(browserClient);
        var secondResponse = await SendTwoFactorLoginRequestAsync(browserClient, code);

        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
    }

    // SS §11 : le journal note l'adresse IP de la requête qui donne le code, pour un refus comme pour une réussite.
    [Fact]
    public async Task JournalKeepsTheIpAddressOfTheRequestThatGivesTheCode()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        browserClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.7");
        await SendLoginRequestAsync(browserClient);

        await SendTwoFactorLoginRequestAsync(browserClient, FindWrongCode(api, activatedAccount.Secret));
        await SendTwoFactorLoginRequestAsync(browserClient, CalculateCurrentCode(api, activatedAccount.Secret));

        var securityEvents = api.SecurityEventRepository.SavedSecurityEvents;
        Assert.Equal(2, securityEvents.Count);
        foreach (var securityEvent in securityEvents)
        {
            Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEvent.IpAddress);
        }
    }

    // --- Les refus

    // 400 avec le champ Code : le site reste sur le formulaire du code, et le cookie d'attente ne bouge pas.
    [Fact]
    public async Task WrongCodeAnswersWithAFieldErrorAndKeepsTheWaitingCookie()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        var wrongCodeResponse = await SendTwoFactorLoginRequestAsync(browserClient, FindWrongCode(api, activatedAccount.Secret));

        Assert.Equal(HttpStatusCode.BadRequest, wrongCodeResponse.StatusCode);
        Assert.Equal("application/problem+json", wrongCodeResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ErrorMessages.WrongTwoFactorCode, await ReadFieldErrorAsync(wrongCodeResponse, "Code"));
        Assert.Null(FindCookie(wrongCodeResponse, WaitingLoginCookieName));
        Assert.Null(FindCookie(wrongCodeResponse, SessionCookiesConfiguration.SessionCookieName));
        SecurityHeadersAssertions.AssertExactSecurityHeaders(wrongCodeResponse);

        var rightCodeResponse = await SendTwoFactorLoginRequestAsync(browserClient, CalculateCurrentCode(api, activatedAccount.Secret));
        Assert.Equal(HttpStatusCode.OK, rightCodeResponse.StatusCode);
    }

    // 401 : le site revient au formulaire du mot de passe, et le cookie d'attente disparaît.
    [Fact]
    public async Task FifthWrongCodeAnswers401AndDeletesTheWaitingCookie()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);
        var wrongCode = FindWrongCode(api, activatedAccount.Secret);

        for (var attemptNumber = 1; attemptNumber <= 4; attemptNumber++)
        {
            var wrongCodeResponse = await SendTwoFactorLoginRequestAsync(browserClient, wrongCode);
            Assert.Equal(HttpStatusCode.BadRequest, wrongCodeResponse.StatusCode);
        }

        var fifthResponse = await SendTwoFactorLoginRequestAsync(browserClient, wrongCode);

        await AssertProblemTitleAsync(fifthResponse, HttpStatusCode.Unauthorized, ErrorMessages.LoginMustRestart);
        Assert.True(FindCookie(fifthResponse, WaitingLoginCookieName)!.Expires < DateTimeOffset.UtcNow);
        Assert.Empty(api.SessionRepository.SavedSessions);
    }

    [Fact]
    public async Task CodeWithoutAnyWaitingLoginAnswers401()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var codeResponse = await SendTwoFactorLoginRequestAsync(browserClient, "123456");

        await AssertProblemTitleAsync(codeResponse, HttpStatusCode.Unauthorized, ErrorMessages.LoginMustRestart);
    }

    [Fact]
    public async Task ForgedWaitingLoginCookieAnswers401AndIsNotCounted()
    {
        using var api = new ApiFactoryWithFakes();
        var attackerClient = CreateClientWithManualCookies(api, WaitingLoginCookieName + "=jeton-invente");

        var codeResponse = await SendTwoFactorLoginRequestAsync(attackerClient, "123456");

        await AssertProblemTitleAsync(codeResponse, HttpStatusCode.Unauthorized, ErrorMessages.LoginMustRestart);
        Assert.Empty(api.SecurityEventRepository.SavedSecurityEvents);
    }

    [Fact]
    public async Task ExpiredWaitingLoginAnswers401EvenWithTheRightCode()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        api.Clock.Advance(TimeSpan.FromMinutes(5));
        var codeResponse = await SendTwoFactorLoginRequestAsync(browserClient, CalculateCurrentCode(api, activatedAccount.Secret));

        await AssertProblemTitleAsync(codeResponse, HttpStatusCode.Unauthorized, ErrorMessages.LoginMustRestart);
    }

    // SS §3 : une récupération pendant l'attente annule cette connexion. Le voleur du mot de passe ne garde rien.
    [Fact]
    public async Task PasswordRecoveryDuringTheWaitCancelsTheWaitingLogin()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var thiefClient = CreateBrowserClient(api);
        var ownerClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(thiefClient);

        var linkToken = await RequestRecoveryThenReadTokenAsync(api, ownerClient, Email);
        var response = await SendResetPasswordRequestAsync(ownerClient, linkToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var thiefResponse = await SendTwoFactorLoginRequestAsync(thiefClient, CalculateCurrentCode(api, activatedAccount.Secret));

        await AssertProblemTitleAsync(thiefResponse, HttpStatusCode.Unauthorized, ErrorMessages.LoginMustRestart);
        Assert.Empty(api.SessionRepository.SavedSessions);
    }

    // GR Auth : la récupération n'enlève pas le code. Une boîte mail piratée ne suffit donc pas à entrer.
    [Fact]
    public async Task AfterAPasswordRecoveryTheLoginStillAsksForTheCode()
    {
        using var api = new ApiFactoryWithFakes();
        await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);
        await SendResetPasswordRequestAsync(browserClient, linkToken);

        var loginResponse = await SendLoginRequestAsync(browserClient, Email, NewPassword);

        var jsonDocument = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        Assert.True(jsonDocument.RootElement.GetProperty("isTwoFactorCodeRequired").GetBoolean());
        Assert.Empty(api.SessionRepository.SavedSessions);
    }

    // --- Les requêtes mal formées

    [Fact]
    public async Task MissingCodeAnswersWithTheStableFieldError()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var codeResponse = await browserClient.PostAsJsonAsync(TwoFactorLoginEndpoint, new { });

        Assert.Equal(HttpStatusCode.BadRequest, codeResponse.StatusCode);
        Assert.Equal("Le code est obligatoire.", await ReadFieldErrorAsync(codeResponse, "Code"));
    }

    [Theory]
    [InlineData("{\"code\": 123456}")]
    [InlineData("{\"code\": [\"123456\"]}")]
    [InlineData("{\"code\": null}")]
    [InlineData("{\"code\": \"\\uD800\"}")]
    [InlineData("pas du json")]
    [InlineData("")]
    public async Task UnreadableBodyAnswers400WithoutRecopyingWhatWasSent(string requestBody)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var codeResponse = await browserClient.PostAsync(TwoFactorLoginEndpoint, new StringContent(requestBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, codeResponse.StatusCode);
        Assert.DoesNotContain("123456", await codeResponse.Content.ReadAsStringAsync());
    }

    // SS §6 : cette route n'accepte que du JSON, qu'un site piégé ne peut pas envoyer sans autorisation.
    [Fact]
    public async Task FormSentByATrappedWebsiteIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await browserClient.PostAsync(TwoFactorLoginEndpoint, new FormUrlEncodedContent(new Dictionary<string, string> { { "code", "123456" } }));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task GetRequestOnTheRouteOfTheCodeIsNotAllowed()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await browserClient.GetAsync(TwoFactorLoginEndpoint);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // --- La limite de débit

    // SS §6 : 10 essais par minute et par adresse IP. Cette route a son propre compteur, distinct de celui du mot de passe.
    [Fact]
    public async Task EleventhAttemptOfTheMinuteFromOneAddressIsRefusedAndOtherAddressesStillPass()
    {
        using var api = new ApiFactoryWithFakes();
        var firstClient = CreateBrowserClient(api);
        firstClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.10");
        var secondClient = CreateBrowserClient(api);
        secondClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.11");

        for (var attemptNumber = 1; attemptNumber <= 10; attemptNumber++)
        {
            var allowedResponse = await SendTwoFactorLoginRequestAsync(firstClient, "123456");
            Assert.Equal(HttpStatusCode.Unauthorized, allowedResponse.StatusCode);
        }

        var overLimitResponse = await SendTwoFactorLoginRequestAsync(firstClient, "123456");
        await AssertProblemTitleAsync(overLimitResponse, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");

        var otherAddressResponse = await SendTwoFactorLoginRequestAsync(secondClient, "123456");
        Assert.Equal(HttpStatusCode.Unauthorized, otherAddressResponse.StatusCode);

        var passwordRouteResponse = await SendLoginRequestAsync(firstClient, "adresse-inconnue@exemple.test");
        Assert.Equal(HttpStatusCode.Unauthorized, passwordRouteResponse.StatusCode);
    }

    [Fact]
    public void LimitOfTheCodeAttemptsIsWrittenWithItsExactValues()
    {
        var codeLimit = RateLimitingConfiguration.CreateTwoFactorCodeLimit();

        Assert.Equal(10, codeLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), codeLimit.Window);
    }

    // --- Les pannes, vues du visiteur

    // Une panne de la base répond 500, sans détail, et n'ouvre rien (SS §10).
    [Fact]
    public async Task DatabaseFailureAnswersAGenericErrorInProduction()
    {
        using var productionApi = new ApiFactoryInProduction();
        var activatedAccount = await AddActivatedAccountAsync(productionApi);
        var browserClient = CreateBrowserClient(productionApi);
        await SendLoginRequestAsync(browserClient);
        productionApi.SessionRepository.IsDown = true;

        var response = await SendTwoFactorLoginRequestAsync(browserClient, CalculateCurrentCode(productionApi, activatedAccount.Secret));

        await AssertProblemTitleAsync(response, HttpStatusCode.InternalServerError, "Une erreur est survenue. Réessaie plus tard.");
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeUserSessionRepository.FailureDetail, responseText);
        Assert.Null(FindCookie(response, SessionCookiesConfiguration.SessionCookieName));
    }

    // Une clé perdue ne se voit pas comme un mauvais code : le visiteur reçoit 500, sans détail, et la panne va dans le journal technique.
    [Fact]
    public async Task LostEncryptionKeyAnswersAGenericErrorAndIsWrittenInTheTechnicalLog()
    {
        using var productionApi = new ApiFactoryInProduction();
        var activatedAccount = await AddActivatedAccountAsync(productionApi);
        activatedAccount.UserAccount.ActivateTwoFactor(new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateEncryptionKey()).Encrypt(activatedAccount.Secret, activatedAccount.UserAccount.Id));
        var browserClient = CreateBrowserClient(productionApi);
        await SendLoginRequestAsync(browserClient);

        var codeResponse = await SendTwoFactorLoginRequestAsync(browserClient, CalculateCurrentCode(productionApi, activatedAccount.Secret));

        await AssertProblemTitleAsync(codeResponse, HttpStatusCode.InternalServerError, "Une erreur est survenue. Réessaie plus tard.");
        var logText = string.Join("\n", productionApi.LoggerProvider.Messages);
        Assert.Contains("AuthenticationTagMismatchException", logText);
        Assert.DoesNotContain(activatedAccount.Secret, logText);
    }

    // --- Le journal technique

    // Au niveau le plus détaillé, le journal technique ne contient jamais de code ni de jeton (SS §7).
    [Fact]
    public async Task TechnicalLogNeverContainsTheCodesOrTheTokensOfTheLogin()
    {
        using var api = new ApiFactoryLoggingEverything();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var wrongCode = FindWrongCode(api, activatedAccount.Secret);
        var rightCode = CalculateCurrentCode(api, activatedAccount.Secret);

        var loginResponse = await SendLoginRequestAsync(browserClient);
        await SendTwoFactorLoginRequestAsync(browserClient, wrongCode);
        var rightCodeResponse = await SendTwoFactorLoginRequestAsync(browserClient, rightCode);

        await SendLoginRequestAsync(browserClient);
        await SendTwoFactorLoginRequestAsync(browserClient, activatedAccount.BackupCode);

        var logText = string.Join("\n", api.LoggerProvider.Messages);
        Assert.NotEmpty(logText);
        Assert.DoesNotContain(wrongCode, logText);
        Assert.DoesNotContain(rightCode, logText);
        Assert.DoesNotContain(activatedAccount.BackupCode, logText);
        Assert.DoesNotContain(activatedAccount.Secret, logText);
        Assert.DoesNotContain(FindCookie(loginResponse, WaitingLoginCookieName)!.Value.ToString(), logText);
        Assert.DoesNotContain(FindCookie(rightCodeResponse, SessionCookiesConfiguration.SessionCookieName)!.Value.ToString(), logText);
    }
}
