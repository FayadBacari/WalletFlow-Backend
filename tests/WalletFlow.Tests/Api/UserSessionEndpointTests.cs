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
    private static async Task<string> LogInThenReadTheSessionCookieAsync(HttpClient clientThatKeepsTheCookiesOfTheBrowser)
    {
        var answerToTheVisitorLoginRequest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        Assert.Equal(HttpStatusCode.OK, answerToTheVisitorLoginRequest.StatusCode);

        return "__Host-walletflow-session=" + FindCookieWrittenByTheAnswer(answerToTheVisitorLoginRequest, "__Host-walletflow-session")!.Value;
    }

    // --- Une session est nécessaire

    // UserSessionController n'a aucun attribut : ce refus prouve que toute route demande une session par défaut.
    [Fact]
    public async Task RouteOfLoggedInUserRefusesVisitorWithoutSession()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientWithoutAnyOpenUserSession = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerGivenWithoutAnyUserSession = await clientWithoutAnyOpenUserSession.GetAsync(AddressOfTheCsrfTokenEndpoint);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerGivenWithoutAnyUserSession, HttpStatusCode.Unauthorized, "Connecte-toi pour continuer.");
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerGivenWithoutAnyUserSession);
    }

    [Fact]
    public async Task ErrorWithoutSessionIsAlsoFrenchInProductionMode()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var clientWithoutAnyOpenUserSession = CreateClientThatKeepsCookiesLikeABrowser(apiStartedLikeInProductionForThisTest);

        var answerGivenWithoutAnyUserSession = await clientWithoutAnyOpenUserSession.GetAsync(AddressOfTheCsrfTokenEndpoint);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerGivenWithoutAnyUserSession, HttpStatusCode.Unauthorized, UserSessionAuthenticationHandler.MessageShownWhenTheVisitorMustLogIn);
    }

    // Sans session, le refus arrive avant le contrôle du jeton anti-CSRF : un visiteur reçoit « connecte-toi ».
    [Fact]
    public async Task LogoutWithoutAnySessionIsAnsweredWithUnauthorizedStatus()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientWithoutAnyOpenUserSession = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheUserLogoutRequest = await SendLogoutRequestWithThisCsrfTokenAsync(clientWithoutAnyOpenUserSession, null);

        Assert.Equal(HttpStatusCode.Unauthorized, answerToTheUserLogoutRequest.StatusCode);
    }

    // ASP.NET Core ne transmet pas un cookie vide : il ne provoque donc aucune lecture de la base.
    [Fact]
    public async Task EmptySessionCookieIsNeverSearchedInTheDatabase()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatSendsAnEmptyCookie = CreateClientThatOnlySendsHandwrittenCookies(apiStartedInMemoryForThisTest, "__Host-walletflow-session=");

        var answerWithTheEmptySessionCookie = await clientThatSendsAnEmptyCookie.GetAsync(AddressOfTheCsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, answerWithTheEmptySessionCookie.StatusCode);
        Assert.Equal(0, apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.NumberOfSearchesOfSessionsInTheDatabase);
    }

    // La limite de débit passe avant toute lecture de la base : un robot arrêté ne coûte plus rien au serveur.
    [Fact]
    public async Task RequestOverTheRateLimitNeverReadsTheSessionInTheDatabase()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatSendsASessionCookie = CreateClientThatOnlySendsHandwrittenCookies(apiStartedInMemoryForThisTest, "__Host-walletflow-session=faux-jeton");

        for (var numberOfTheAttemptBeingSent = 1; numberOfTheAttemptBeingSent <= 10; numberOfTheAttemptBeingSent++)
        {
            await SendLoginRequestToTheApiAsync(clientThatSendsASessionCookie, "inconnu@exemple.test");
        }

        var numberOfSearchesBeforeTheLimit = apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.NumberOfSearchesOfSessionsInTheDatabase;
        var answerOverTheRateLimitOfTheLogin = await SendLoginRequestToTheApiAsync(clientThatSendsASessionCookie, "inconnu@exemple.test");

        Assert.Equal(HttpStatusCode.TooManyRequests, answerOverTheRateLimitOfTheLogin.StatusCode);
        Assert.Equal(numberOfSearchesBeforeTheLimit, apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.NumberOfSearchesOfSessionsInTheDatabase);
    }

    // Un client qui refuse le JSON ne doit pas provoquer une erreur 500 : le code d'erreur reste juste.
    [Fact]
    public async Task ClientThatRefusesJsonStillGetsUnauthorized()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatRefusesJsonInTheAnswers = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientThatRefusesJsonInTheAnswers.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/html"));

        var answerToTheClientThatRefusesJson = await clientThatRefusesJsonInTheAnswers.GetAsync(AddressOfTheCsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, answerToTheClientThatRefusesJson.StatusCode);
    }

    // Aucune de ces valeurs ne doit provoquer d'erreur 500, ni ouvrir une session.
    [Theory]
    [InlineData("__Host-walletflow-session=")]
    [InlineData("__Host-walletflow-session=faux-jeton")]
    [InlineData("__Host-walletflow-session=%00%FF%FE")]
    [InlineData("__Host-walletflow-session=' OR 1=1 --")]
    [InlineData("__Host-walletflow-session=" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("walletflow-session=faux-jeton")]
    public async Task JunkSessionCookieIsAnsweredWithUnauthorizedStatus(string cookiesWrittenByHandInTheTest)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatSendsAJunkCookie = CreateClientThatOnlySendsHandwrittenCookies(apiStartedInMemoryForThisTest, cookiesWrittenByHandInTheTest);

        var answerWithTheJunkSessionCookie = await clientThatSendsAJunkCookie.GetAsync(AddressOfTheCsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, answerWithTheJunkSessionCookie.StatusCode);
    }

    // La base ne contient que l'empreinte : quelqu'un qui lit la base ne peut pas s'en servir comme cookie.
    [Fact]
    public async Task HashReadInTheDatabaseCannotBeUsedAsSessionCookie()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest));
        var hashOfTheSessionReadInTheDatabase = Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions).Id;
        var clientThatReadTheHashInTheDatabase = CreateClientThatOnlySendsHandwrittenCookies(apiStartedInMemoryForThisTest, "__Host-walletflow-session=" + hashOfTheSessionReadInTheDatabase);

        var answerWithTheHashOfTheDatabase = await clientThatReadTheHashInTheDatabase.GetAsync(AddressOfTheCsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, answerWithTheHashOfTheDatabase.StatusCode);
    }

    // Le compte supprimé emporte ses sessions (ON DELETE CASCADE, testé sur la vraie base).
    [Fact]
    public async Task SessionDeletedFromTheServerNoLongerWorks()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions.Clear();
        var answerAfterTheDeletionOfTheSession = await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, answerAfterTheDeletionOfTheSession.StatusCode);
    }

    // --- 30 minutes d'inactivité

    [Fact]
    public async Task SessionStaysOpenWhileItIsUsedAndClosesAfterThirtyMinutesWithoutActivity()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint)).StatusCode);

        apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint)).StatusCode);

        apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint)).StatusCode);
    }

    // --- Jeton anti-CSRF

    [Fact]
    public async Task CsrfTokenRouteGivesTokenThatIsNotKeptInAnyCache()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheCsrfTokenRequest = await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.OK, answerToTheCsrfTokenRequest.StatusCode);
        Assert.True(answerToTheCsrfTokenRequest.Headers.CacheControl!.NoStore);
        Assert.False(answerToTheCsrfTokenRequest.Headers.Contains("X-Frame-Options"));
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheCsrfTokenRequest);
    }

    // Par défaut, ASP.NET Core ne met pas Secure sur ce cookie : le réglage doit être vérifié.
    [Fact]
    public async Task CsrfCookieHasItsExactAttributes()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheCsrfTokenRequest = await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint);

        var csrfCookieWrittenByTheAnswer = FindCookieWrittenByTheAnswer(answerToTheCsrfTokenRequest, "__Host-walletflow-csrf");
        Assert.NotNull(csrfCookieWrittenByTheAnswer);
        Assert.True(csrfCookieWrittenByTheAnswer.Secure);
        Assert.True(csrfCookieWrittenByTheAnswer.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, csrfCookieWrittenByTheAnswer.SameSite);
        Assert.Equal("/", csrfCookieWrittenByTheAnswer.Path.ToString());
        Assert.Equal(0, csrfCookieWrittenByTheAnswer.Domain.Length);
    }

    // Un site piégé envoie le cookie de session mais ne peut pas lire le jeton : sa requête est refusée.
    [Fact]
    public async Task LogoutWithoutCsrfTokenIsRefusedAndKeepsTheSession()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerGivenWithoutAnyCsrfToken = await SendLogoutRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, null);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerGivenWithoutAnyCsrfToken, HttpStatusCode.BadRequest, ApiErrorConfiguration.TitleOfEveryRefusedRequestAnswer);
        Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    [Fact]
    public async Task LogoutWithInventedCsrfTokenIsRefusedAndKeepsTheSession()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerWithInventedCsrfTokenInHeader = await SendLogoutRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, "jeton-invente");

        Assert.Equal(HttpStatusCode.BadRequest, answerWithInventedCsrfTokenInHeader.StatusCode);
        Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Le jeton est lié à l'utilisateur : celui d'un compte ne sert pas à un autre compte.
    [Fact]
    public async Task CsrfTokenOfAnotherUserIsRefused()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, "autre-utilisateur@exemple.test");
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var csrfTokenOfTheFirstUser = await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatKeepsTheCookiesOfTheBrowser);

        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "autre-utilisateur@exemple.test");
        var answerWithTheTokenOfTheFirstUser = await SendLogoutRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheFirstUser);

        Assert.Equal(HttpStatusCode.BadRequest, answerWithTheTokenOfTheFirstUser.StatusCode);
    }

    // SS §6 : le jeton n'est lu que dans l'en-tête, jamais dans un formulaire qu'un site piégé pourrait construire.
    [Fact]
    public async Task CsrfTokenSentInAFormIsNotAccepted()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var csrfTokenOfTheLoggedInUser = await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var formContainingTheCsrfTokenOfTheUser = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("__RequestVerificationToken", csrfTokenOfTheLoggedInUser) });

        var answerToTheFormOfTheBrowser = await clientThatKeepsTheCookiesOfTheBrowser.PostAsync(AddressOfTheUserLogoutEndpoint, formContainingTheCsrfTokenOfTheUser);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheFormOfTheBrowser.StatusCode);
        Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // --- Déconnexion

    [Fact]
    public async Task LogoutDestroysTheServerSessionAndDeletesTheSessionCookie()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var csrfTokenOfTheLoggedInUser = await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheUserLogoutRequest = await SendLogoutRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);

        Assert.Equal(HttpStatusCode.OK, answerToTheUserLogoutRequest.StatusCode);
        Assert.Contains(UserSessionController.MessageSentWhenTheUserIsLoggedOut, await answerToTheUserLogoutRequest.Content.ReadAsStringAsync());
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);

        // Avec le préfixe __Host-, un navigateur n'efface le cookie que si Secure et le chemin / sont présents.
        var deletionOfTheUserSessionCookie = FindCookieWrittenByTheAnswer(answerToTheUserLogoutRequest, "__Host-walletflow-session");
        Assert.NotNull(deletionOfTheUserSessionCookie);
        Assert.True(deletionOfTheUserSessionCookie.Expires < DateTimeOffset.UtcNow);
        Assert.True(deletionOfTheUserSessionCookie.Secure);
        Assert.Equal("/", deletionOfTheUserSessionCookie.Path.ToString());

        // Le navigateur reste reconnu à la prochaine connexion.
        Assert.Null(FindCookieWrittenByTheAnswer(answerToTheUserLogoutRequest, "__Host-walletflow-device"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint)).StatusCode);
    }

    // SS §3 : la session est détruite côté serveur, pas seulement le cookie. Un cookie copié avant ne vaut plus rien.
    [Fact]
    public async Task CookieCopiedBeforeTheLogoutNoLongerWorksAfterIt()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var sessionCookieCopiedBeforeTheLogout = await LogInThenReadTheSessionCookieAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var clientWithTheCopiedSessionCookie = CreateClientThatOnlySendsHandwrittenCookies(apiStartedInMemoryForThisTest, sessionCookieCopiedBeforeTheLogout);
        Assert.Equal(HttpStatusCode.OK, (await clientWithTheCopiedSessionCookie.GetAsync(AddressOfTheCsrfTokenEndpoint)).StatusCode);

        var csrfTokenOfTheLoggedInUser = await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatKeepsTheCookiesOfTheBrowser);
        await SendLogoutRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);

        Assert.Equal(HttpStatusCode.Unauthorized, (await clientWithTheCopiedSessionCookie.GetAsync(AddressOfTheCsrfTokenEndpoint)).StatusCode);
    }

    // Une deuxième déconnexion ne trouve plus de session.
    [Fact]
    public async Task SecondLogoutIsAnsweredWithUnauthorizedStatus()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var csrfTokenOfTheLoggedInUser = await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatKeepsTheCookiesOfTheBrowser);
        await SendLogoutRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);

        var answerToTheSecondLogoutRequest = await SendLogoutRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);

        Assert.Equal(HttpStatusCode.Unauthorized, answerToTheSecondLogoutRequest.StatusCode);
    }

    // Se déconnecter dans un navigateur ne ferme pas la session de l'autre.
    [Fact]
    public async Task LogoutClosesOnlyTheSessionOfThisBrowser()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatPlaysTheFirstBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var clientThatPlaysTheSecondBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatPlaysTheFirstBrowser);
        await SendLoginRequestToTheApiAsync(clientThatPlaysTheSecondBrowser);
        var csrfTokenOfTheFirstBrowser = await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatPlaysTheFirstBrowser);

        await SendLogoutRequestWithThisCsrfTokenAsync(clientThatPlaysTheFirstBrowser, csrfTokenOfTheFirstBrowser);

        Assert.Equal(HttpStatusCode.OK, (await clientThatPlaysTheSecondBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint)).StatusCode);
    }

    // --- Pannes

    // Une panne ne doit jamais laisser passer : sans session lisible, la route répond une erreur générique (SS §10).
    [Fact]
    public async Task DatabaseFailureWhileReadingTheSessionIsAnsweredWithoutAnyTechnicalDetail()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        apiStartedLikeInProductionForThisTest.RepositoryThatKeepsSessionsInMemory.DatabaseIsDownDuringThisTest = true;
        var clientThatSendsASessionCookie = CreateClientThatOnlySendsHandwrittenCookies(apiStartedLikeInProductionForThisTest, "__Host-walletflow-session=jeton");

        var answerDuringTheSimulatedDatabaseFailure = await clientThatSendsASessionCookie.GetAsync(AddressOfTheCsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.InternalServerError, answerDuringTheSimulatedDatabaseFailure.StatusCode);
        Assert.DoesNotContain(Fakes.FakeUserSessionRepository.TechnicalDetailOfTheSimulatedSessionFailure, await answerDuringTheSimulatedDatabaseFailure.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DatabaseFailureDuringLoginIsAnsweredWithoutCookieNorTechnicalDetail()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedLikeInProductionForThisTest);
        apiStartedLikeInProductionForThisTest.RepositoryThatKeepsSessionsInMemory.DatabaseIsDownDuringThisTest = true;
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedLikeInProductionForThisTest);

        var answerDuringTheSimulatedDatabaseFailure = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        Assert.Equal(HttpStatusCode.InternalServerError, answerDuringTheSimulatedDatabaseFailure.StatusCode);
        Assert.False(answerDuringTheSimulatedDatabaseFailure.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(Fakes.FakeUserSessionRepository.TechnicalDetailOfTheSimulatedSessionFailure, await answerDuringTheSimulatedDatabaseFailure.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task JournalFailureDuringRefusedLoginIsAnsweredWithoutCookieNorTechnicalDetail()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedLikeInProductionForThisTest);
        apiStartedLikeInProductionForThisTest.RepositoryThatKeepsSecurityEventsInMemory.DatabaseIsDownDuringThisTest = true;
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedLikeInProductionForThisTest);

        var answerDuringTheSimulatedJournalFailure = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, WrongPasswordTypedInTheLoginTests);

        Assert.Equal(HttpStatusCode.InternalServerError, answerDuringTheSimulatedJournalFailure.StatusCode);
        Assert.False(answerDuringTheSimulatedJournalFailure.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(Fakes.FakeSecurityEventRepository.TechnicalDetailOfTheSimulatedJournalFailure, await answerDuringTheSimulatedJournalFailure.Content.ReadAsStringAsync());
    }

    // --- Services réels

    // Les autres tests remplacent la base : celui-ci vérifie que l'API utilise bien les vrais accès à la base.
    [Fact]
    public void ApiUsesTheRealRepositoriesWhenNothingIsReplaced()
    {
        using var apiStartedWithItsRealServices = new WebApplicationFactory<Program>().WithWebHostBuilder(builderOfTheApiWithItsRealServices =>
        {
            // Cette base n'est jamais contactée par ce test : les tâches de fond notent seulement leur panne dans le journal technique.
            builderOfTheApiWithItsRealServices.UseSetting("ConnectionStrings:WalletFlowDatabase", "Host=localhost;Database=base_jamais_contactee");
            builderOfTheApiWithItsRealServices.UseSetting("TwoFactorSecretEncryptionKey", TwoFactorTestTools.CreateNewEncryptionKeyForOneTest());
        });

        using var scopeOfTheRealServicesOfTheApi = apiStartedWithItsRealServices.Services.CreateScope();

        Assert.IsType<UserSessionRepository>(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<IUserSessionRepository>());
        Assert.IsType<SecurityEventRepository>(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<ISecurityEventRepository>());
        Assert.IsType<TwoFactorAuthenticationRepository>(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<ITwoFactorAuthenticationRepository>());
        Assert.IsType<OtpNetAuthenticatorCodeTool>(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<IAuthenticatorCodeTool>());
        Assert.IsType<AesGcmTotpSecretProtector>(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<ITotpSecretProtector>());
        Assert.IsType<InMemoryLoginsWaitingForTheirCodeStore>(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<ILoginsWaitingForTheirCodeStore>());
        Assert.IsType<InMemorySecretsWaitingForActivationStore>(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<ISecretsWaitingForActivationStore>());
        Assert.IsType<InMemoryUsedAuthenticatorCodesRegister>(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<IUsedAuthenticatorCodesRegister>());
        Assert.NotNull(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<TwoFactorLoginService>());
        Assert.NotNull(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<TwoFactorActivationService>());
        Assert.NotNull(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<TwoFactorDeactivationService>());
        Assert.NotNull(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<AccountLoginService>());
        Assert.NotNull(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<UserSessionService>());
        Assert.NotNull(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<ExpiredUserSessionsCleanupService>());
        Assert.NotNull(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<PasswordRecoveryRequestService>());
        Assert.NotNull(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<PasswordResetService>());
        Assert.NotNull(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<AccountUnlockService>());
        Assert.NotNull(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<BackgroundEmailRequestProcessingService>());
        Assert.IsType<InMemoryBackgroundEmailRequestQueue>(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<IBackgroundEmailRequestQueue>());
        Assert.IsType<InMemoryPasswordRecoveryRequestLimiter>(scopeOfTheRealServicesOfTheApi.ServiceProvider.GetRequiredService<IPasswordRecoveryRequestLimiter>());
    }

    // Le nom de l'en-tête est un contrat avec le site : il ne doit pas changer sans qu'on le voie.
    [Fact]
    public void CsrfTokenIsReadInTheExpectedHeader()
    {
        Assert.Equal("X-CSRF-TOKEN", AntiforgeryConfiguration.NameOfTheHeaderCarryingTheCsrfToken);
    }

    // --- Liste des routes publiques

    // Donne les routes de l'API qui portent cet attribut.
    private static List<string> ListRoutesCarryingThisAttribute<TAttribute>(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest)
    {
        var providerOfTheRoutesOfTheApi = apiStartedInMemoryForThisTest.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var routesOfTheApiCarryingThisAttribute = new List<string>();

        foreach (var oneRouteOfTheWalletFlowApi in providerOfTheRoutesOfTheApi.ActionDescriptors.Items)
        {
            if (oneRouteOfTheWalletFlowApi.EndpointMetadata.OfType<TAttribute>().Any())
            {
                routesOfTheApiCarryingThisAttribute.Add(oneRouteOfTheWalletFlowApi.AttributeRouteInfo!.Template!);
            }
        }

        routesOfTheApiCarryingThisAttribute.Sort(StringComparer.Ordinal);
        return routesOfTheApiCarryingThisAttribute;
    }

    // Ajouter une route publique doit être un choix conscient : ce test oblige à la déclarer ici.
    [Fact]
    public void OnlyTheSevenExpectedRoutesCanBeCalledWithoutSession()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var routesExpectedToBePublicInTheApi = new List<string> { "api/account/confirm-email", "api/account/forgot-password", "api/account/login", "api/account/login/two-factor", "api/account/register", "api/account/reset-password", "api/account/unlock" };

        Assert.Equal(routesExpectedToBePublicInTheApi, ListRoutesCarryingThisAttribute<Microsoft.AspNetCore.Authorization.IAllowAnonymous>(apiStartedInMemoryForThisTest));
    }

    // SS §6 : seules les routes appelées sans session n'ont pas besoin du jeton anti-CSRF.
    [Fact]
    public void OnlyTheSevenExpectedRoutesIgnoreTheCsrfToken()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var routesExpectedToBePublicInTheApi = new List<string> { "api/account/confirm-email", "api/account/forgot-password", "api/account/login", "api/account/login/two-factor", "api/account/register", "api/account/reset-password", "api/account/unlock" };

        Assert.Equal(routesExpectedToBePublicInTheApi, ListRoutesCarryingThisAttribute<IgnoreAntiforgeryTokenAttribute>(apiStartedInMemoryForThisTest));
    }
}
