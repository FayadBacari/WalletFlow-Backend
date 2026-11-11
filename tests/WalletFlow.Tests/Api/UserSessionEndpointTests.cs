using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Controllers;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserSessions;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les sessions et le jeton anti-CSRF de bout en bout (GR Auth, SS §3, §4 et §6).
public class UserSessionEndpointTests
{
    // Connecte le compte des tests et renvoie le cookie de session, tel qu'il a été écrit par la réponse.
    private static async Task<string> LogInThenReadSessionCookieAsync(HttpClient browserClient)
    {
        var response = await SendLoginRequestAsync(browserClient);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return "__Host-walletflow-session=" + FindCookie(response, "__Host-walletflow-session")!.Value;
    }

    // --- Une session est nécessaire

    // UserSessionController n'a aucun attribut : ce refus prouve que toute route demande une session par défaut.
    [Fact]
    public async Task RouteOfLoggedInUserRefusesVisitorWithoutSession()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateBrowserClient(api);

        var response = await client.GetAsync(CsrfTokenEndpoint);

        await AssertProblemTitleAsync(response, HttpStatusCode.Unauthorized, "Connecte-toi pour continuer.");
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    [Fact]
    public async Task ErrorWithoutSessionIsAlsoFrenchInProductionMode()
    {
        using var productionApi = new ApiFactoryInProduction();
        var client = CreateBrowserClient(productionApi);

        var response = await client.GetAsync(CsrfTokenEndpoint);

        await AssertProblemTitleAsync(response, HttpStatusCode.Unauthorized, UserSessionAuthenticationHandler.LoginRequiredMessage);
    }

    // Sans session, le refus arrive avant le contrôle du jeton anti-CSRF : un visiteur reçoit « connecte-toi ».
    [Fact]
    public async Task LogoutWithoutAnySessionIsAnsweredWithUnauthorizedStatus()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateBrowserClient(api);

        var response = await SendLogoutRequestAsync(client, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ASP.NET Core ne transmet pas un cookie vide : il ne provoque donc aucune lecture de la base.
    [Fact]
    public async Task EmptySessionCookieIsNeverSearchedInTheDatabase()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateClientWithManualCookies(api, "__Host-walletflow-session=");

        var response = await client.GetAsync(CsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, api.SessionRepository.SessionSearchCount);
    }

    // La limite de débit passe avant toute lecture de la base : un robot arrêté ne coûte plus rien au serveur.
    [Fact]
    public async Task RequestOverTheRateLimitNeverReadsTheSessionInTheDatabase()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateClientWithManualCookies(api, "__Host-walletflow-session=faux-jeton");

        for (var attemptNumber = 1; attemptNumber <= 10; attemptNumber++)
        {
            await SendLoginRequestAsync(client, "inconnu@exemple.test");
        }

        var searcheCount = api.SessionRepository.SessionSearchCount;
        var response = await SendLoginRequestAsync(client, "inconnu@exemple.test");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(searcheCount, api.SessionRepository.SessionSearchCount);
    }

    // Un client qui refuse le JSON ne doit pas provoquer une erreur 500 : le code d'erreur reste juste.
    [Fact]
    public async Task ClientThatRefusesJsonStillGetsUnauthorized()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateBrowserClient(api);
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/html"));

        var response = await client.GetAsync(CsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Aucune de ces valeurs ne doit provoquer d'erreur 500, ni ouvrir une session.
    [Theory]
    [InlineData("__Host-walletflow-session=")]
    [InlineData("__Host-walletflow-session=faux-jeton")]
    [InlineData("__Host-walletflow-session=%00%FF%FE")]
    [InlineData("__Host-walletflow-session=' OR 1=1 --")]
    [InlineData("__Host-walletflow-session=" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("walletflow-session=faux-jeton")]
    public async Task JunkSessionCookieIsAnsweredWithUnauthorizedStatus(string cookies)
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateClientWithManualCookies(api, cookies);

        var response = await client.GetAsync(CsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // La base ne contient que l'empreinte : quelqu'un qui lit la base ne peut pas s'en servir comme cookie.
    [Fact]
    public async Task HashReadInTheDatabaseCannotBeUsedAsSessionCookie()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        await SendLoginRequestAsync(CreateBrowserClient(api));
        var sessionHash = Assert.Single(api.SessionRepository.SavedSessions).Id;
        var client = CreateClientWithManualCookies(api, "__Host-walletflow-session=" + sessionHash);

        var response = await client.GetAsync(CsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Le compte supprimé emporte ses sessions (ON DELETE CASCADE, testé sur la vraie base).
    [Fact]
    public async Task SessionDeletedFromTheServerNoLongerWorks()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        api.SessionRepository.SavedSessions.Clear();
        var response = await browserClient.GetAsync(CsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- 30 minutes d'inactivité

    [Fact]
    public async Task SessionStaysOpenWhileItIsUsedAndClosesAfterThirtyMinutesWithoutActivity()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        api.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await browserClient.GetAsync(CsrfTokenEndpoint)).StatusCode);

        api.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await browserClient.GetAsync(CsrfTokenEndpoint)).StatusCode);

        api.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browserClient.GetAsync(CsrfTokenEndpoint)).StatusCode);
    }

    // --- Jeton anti-CSRF

    [Fact]
    public async Task CsrfTokenRouteGivesTokenThatIsNotKeptInAnyCache()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        var response = await browserClient.GetAsync(CsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.False(response.Headers.Contains("X-Frame-Options"));
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    // Par défaut, ASP.NET Core ne met pas Secure sur ce cookie : le réglage doit être vérifié.
    [Fact]
    public async Task CsrfCookieHasItsExactAttributes()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        var response = await browserClient.GetAsync(CsrfTokenEndpoint);

        var csrfCookie = FindCookie(response, "__Host-walletflow-csrf");
        Assert.NotNull(csrfCookie);
        Assert.True(csrfCookie.Secure);
        Assert.True(csrfCookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, csrfCookie.SameSite);
        Assert.Equal("/", csrfCookie.Path.ToString());
        Assert.Equal(0, csrfCookie.Domain.Length);
    }

    // Un site piégé envoie le cookie de session mais ne peut pas lire le jeton : sa requête est refusée.
    [Fact]
    public async Task LogoutWithoutCsrfTokenIsRefusedAndKeepsTheSession()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        var response = await SendLogoutRequestAsync(browserClient, null);

        await AssertProblemTitleAsync(response, HttpStatusCode.BadRequest, ApiErrorConfiguration.RefusedRequestTitle);
        Assert.Single(api.SessionRepository.SavedSessions);
    }

    [Fact]
    public async Task LogoutWithInventedCsrfTokenIsRefusedAndKeepsTheSession()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);
        await GetCsrfTokenAsync(browserClient);

        var response = await SendLogoutRequestAsync(browserClient, "jeton-invente");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(api.SessionRepository.SavedSessions);
    }

    // Le jeton est lié à l'utilisateur : celui d'un compte ne sert pas à un autre compte.
    [Fact]
    public async Task CsrfTokenOfAnotherUserIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        await AddAccountAsync(api, "autre-utilisateur@exemple.test");
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);
        var csrfToken = await GetCsrfTokenAsync(browserClient);

        await SendLoginRequestAsync(browserClient, "autre-utilisateur@exemple.test");
        var response = await SendLogoutRequestAsync(browserClient, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // SS §6 : le jeton n'est lu que dans l'en-tête, jamais dans un formulaire qu'un site piégé pourrait construire.
    [Fact]
    public async Task CsrfTokenSentInAFormIsNotAccepted()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);
        var csrfToken = await GetCsrfTokenAsync(browserClient);
        var form = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("__RequestVerificationToken", csrfToken) });

        var response = await browserClient.PostAsync(LogoutEndpoint, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(api.SessionRepository.SavedSessions);
    }

    // --- Déconnexion

    [Fact]
    public async Task LogoutDestroysTheServerSessionAndDeletesTheSessionCookie()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);
        var csrfToken = await GetCsrfTokenAsync(browserClient);

        var response = await SendLogoutRequestAsync(browserClient, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(UserSessionController.LoggedOutMessage, await response.Content.ReadAsStringAsync());
        Assert.Empty(api.SessionRepository.SavedSessions);

        // Avec le préfixe __Host-, un navigateur n'efface le cookie que si Secure et le chemin / sont présents.
        var sessionCookieDeletion = FindCookie(response, "__Host-walletflow-session");
        Assert.NotNull(sessionCookieDeletion);
        Assert.True(sessionCookieDeletion.Expires < DateTimeOffset.UtcNow);
        Assert.True(sessionCookieDeletion.Secure);
        Assert.Equal("/", sessionCookieDeletion.Path.ToString());

        // Le navigateur reste reconnu à la prochaine connexion.
        Assert.Null(FindCookie(response, "__Host-walletflow-device"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await browserClient.GetAsync(CsrfTokenEndpoint)).StatusCode);
    }

    // SS §3 : la session est détruite côté serveur, pas seulement le cookie. Un cookie copié avant ne vaut plus rien.
    [Fact]
    public async Task CookieCopiedBeforeTheLogoutNoLongerWorksAfterIt()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var copiedSessionCookie = await LogInThenReadSessionCookieAsync(browserClient);
        var copiedCookieClient = CreateClientWithManualCookies(api, copiedSessionCookie);
        Assert.Equal(HttpStatusCode.OK, (await copiedCookieClient.GetAsync(CsrfTokenEndpoint)).StatusCode);

        var csrfToken = await GetCsrfTokenAsync(browserClient);
        await SendLogoutRequestAsync(browserClient, csrfToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await copiedCookieClient.GetAsync(CsrfTokenEndpoint)).StatusCode);
    }

    // Une deuxième déconnexion ne trouve plus de session.
    [Fact]
    public async Task SecondLogoutIsAnsweredWithUnauthorizedStatus()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);
        var csrfToken = await GetCsrfTokenAsync(browserClient);
        await SendLogoutRequestAsync(browserClient, csrfToken);

        var response = await SendLogoutRequestAsync(browserClient, csrfToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Se déconnecter dans un navigateur ne ferme pas la session de l'autre.
    [Fact]
    public async Task LogoutClosesOnlyTheSessionOfThisBrowser()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var firstClient = CreateBrowserClient(api);
        var secondClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(firstClient);
        await SendLoginRequestAsync(secondClient);
        var csrfToken = await GetCsrfTokenAsync(firstClient);

        await SendLogoutRequestAsync(firstClient, csrfToken);

        Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync(CsrfTokenEndpoint)).StatusCode);
    }

    // --- Pannes

    // Une panne ne doit jamais laisser passer : sans session lisible, la route répond une erreur générique (SS §10).
    [Fact]
    public async Task DatabaseFailureWhileReadingTheSessionIsAnsweredWithoutAnyTechnicalDetail()
    {
        using var productionApi = new ApiFactoryInProduction();
        productionApi.SessionRepository.IsDown = true;
        var client = CreateClientWithManualCookies(productionApi, "__Host-walletflow-session=jeton");

        var response = await client.GetAsync(CsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(Fakes.FakeUserSessionRepository.FailureDetail, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DatabaseFailureDuringLoginIsAnsweredWithoutCookieNorTechnicalDetail()
    {
        using var productionApi = new ApiFactoryInProduction();
        await AddAccountAsync(productionApi);
        productionApi.SessionRepository.IsDown = true;
        var browserClient = CreateBrowserClient(productionApi);

        var response = await SendLoginRequestAsync(browserClient);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(Fakes.FakeUserSessionRepository.FailureDetail, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task JournalFailureDuringRefusedLoginIsAnsweredWithoutCookieNorTechnicalDetail()
    {
        using var productionApi = new ApiFactoryInProduction();
        await AddAccountAsync(productionApi);
        productionApi.SecurityEventRepository.IsDown = true;
        var browserClient = CreateBrowserClient(productionApi);

        var response = await SendLoginRequestAsync(browserClient, Email, WrongPassword);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(Fakes.FakeSecurityEventRepository.FailureDetail, await response.Content.ReadAsStringAsync());
    }

    // --- Services réels

    // Les autres tests remplacent la base : celui-ci vérifie que l'API utilise bien les vrais accès à la base.
    [Fact]
    public void ApiUsesTheRealRepositoriesWhenNothingIsReplaced()
    {
        // La base indiquée n'est jamais contactée par ce test : les tâches de fond notent seulement leur panne dans le journal technique.
        using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(apiBuilder =>
            TestApiSettings.Apply(apiBuilder, TwoFactorTestTools.CreateEncryptionKey()));

        using var scope = api.Services.CreateScope();

        Assert.IsType<UserSessionRepository>(scope.ServiceProvider.GetRequiredService<IUserSessionRepository>());
        Assert.IsType<SecurityEventRepository>(scope.ServiceProvider.GetRequiredService<ISecurityEventRepository>());
        Assert.IsType<TwoFactorAuthenticationRepository>(scope.ServiceProvider.GetRequiredService<ITwoFactorAuthenticationRepository>());
        Assert.IsType<OtpNetAuthenticatorCodeTool>(scope.ServiceProvider.GetRequiredService<IAuthenticatorCodeTool>());
        Assert.IsType<AesGcmTotpSecretProtector>(scope.ServiceProvider.GetRequiredService<ITotpSecretProtector>());
        Assert.IsType<InMemoryWaitingLoginStore>(scope.ServiceProvider.GetRequiredService<IWaitingLoginStore>());
        Assert.IsType<InMemoryWaitingSecretStore>(scope.ServiceProvider.GetRequiredService<IWaitingSecretStore>());
        Assert.IsType<InMemoryUsedAuthenticatorCodesRegister>(scope.ServiceProvider.GetRequiredService<IUsedAuthenticatorCodesRegister>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TwoFactorLoginService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TwoFactorActivationService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TwoFactorDeactivationService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AccountLoginService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<UserSessionService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ExpiredUserSessionsCleanupService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<PasswordRecoveryRequestService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<PasswordResetService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AccountUnlockService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<BackgroundEmailRequestProcessingService>());
        Assert.IsType<InMemoryBackgroundEmailRequestQueue>(scope.ServiceProvider.GetRequiredService<IBackgroundEmailRequestQueue>());
        Assert.IsType<InMemoryPasswordRecoveryRequestLimiter>(scope.ServiceProvider.GetRequiredService<IPasswordRecoveryRequestLimiter>());
    }

    // Le nom de l'en-tête est un contrat avec le site : il ne doit pas changer sans qu'on le voie.
    [Fact]
    public void CsrfTokenIsReadInTheExpectedHeader()
    {
        Assert.Equal("X-CSRF-TOKEN", AntiforgeryConfiguration.CsrfHeaderName);
    }

    // --- Liste des routes publiques

    // Donne les routes de l'API qui portent cet attribut.
    private static List<string> ListRoutesWithAttribute<TAttribute>(ApiFactoryWithFakes api)
    {
        var routeProvider = api.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var routes = new List<string>();

        foreach (var route in routeProvider.ActionDescriptors.Items)
        {
            if (route.EndpointMetadata.OfType<TAttribute>().Any())
            {
                routes.Add(route.AttributeRouteInfo!.Template!);
            }
        }

        routes.Sort(StringComparer.Ordinal);
        return routes;
    }

    // Ajouter une route publique doit être un choix conscient : ce test oblige à la déclarer ici.
    [Fact]
    public void OnlyTheSevenExpectedRoutesCanBeCalledWithoutSession()
    {
        using var api = new ApiFactoryWithFakes();
        var publicRoutes = new List<string> { "api/account/confirm-email", "api/account/forgot-password", "api/account/login", "api/account/login/two-factor", "api/account/register", "api/account/reset-password", "api/account/unlock" };

        Assert.Equal(publicRoutes, ListRoutesWithAttribute<Microsoft.AspNetCore.Authorization.IAllowAnonymous>(api));
    }

    // SS §6 : seules les routes appelées sans session n'ont pas besoin du jeton anti-CSRF.
    [Fact]
    public void OnlyTheSevenExpectedRoutesIgnoreTheCsrfToken()
    {
        using var api = new ApiFactoryWithFakes();
        var publicRoutes = new List<string> { "api/account/confirm-email", "api/account/forgot-password", "api/account/login", "api/account/login/two-factor", "api/account/register", "api/account/reset-password", "api/account/unlock" };

        Assert.Equal(publicRoutes, ListRoutesWithAttribute<IgnoreAntiforgeryTokenAttribute>(api));
    }
}
