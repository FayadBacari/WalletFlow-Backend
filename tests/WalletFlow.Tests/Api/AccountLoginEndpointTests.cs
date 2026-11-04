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
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheVisitorLoginRequest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        Assert.Equal(HttpStatusCode.OK, answerToTheVisitorLoginRequest.StatusCode);
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheVisitorLoginRequest.Content.ReadAsStringAsync());
        Assert.Equal(AccountLoginController.MessageSentWhenTheVisitorIsLoggedIn, jsonDocumentOfTheApiAnswer.RootElement.GetProperty("message").GetString());
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheVisitorLoginRequest);

        var sessionCookieWrittenByTheAnswer = FindCookieWrittenByTheAnswer(answerToTheVisitorLoginRequest, "__Host-walletflow-session");
        Assert.NotNull(sessionCookieWrittenByTheAnswer);
        Assert.True(sessionCookieWrittenByTheAnswer.HttpOnly);
        Assert.True(sessionCookieWrittenByTheAnswer.Secure);
        Assert.Equal(SameSiteMode.Lax, sessionCookieWrittenByTheAnswer.SameSite);
        Assert.Equal("/", sessionCookieWrittenByTheAnswer.Path.ToString());
        Assert.Equal(0, sessionCookieWrittenByTheAnswer.Domain.Length);

        // Sans date de fin, le cookie disparaît à la fermeture du navigateur.
        Assert.Null(sessionCookieWrittenByTheAnswer.MaxAge);
        Assert.Null(sessionCookieWrittenByTheAnswer.Expires);
    }

    [Fact]
    public async Task RightPasswordWritesTheDurableDeviceCookieWithItsExactAttributes()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheVisitorLoginRequest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var deviceCookieWrittenByTheAnswer = FindCookieWrittenByTheAnswer(answerToTheVisitorLoginRequest, "__Host-walletflow-device");
        Assert.NotNull(deviceCookieWrittenByTheAnswer);
        Assert.True(deviceCookieWrittenByTheAnswer.HttpOnly);
        Assert.True(deviceCookieWrittenByTheAnswer.Secure);
        Assert.Equal(SameSiteMode.Lax, deviceCookieWrittenByTheAnswer.SameSite);
        Assert.Equal("/", deviceCookieWrittenByTheAnswer.Path.ToString());
        Assert.Equal(0, deviceCookieWrittenByTheAnswer.Domain.Length);
        Assert.Equal(TimeSpan.FromDays(365), deviceCookieWrittenByTheAnswer.MaxAge);
    }

    // Les jetons ne sont visibles dans aucune réponse lisible par un script : seulement dans les cookies HttpOnly.
    [Fact]
    public async Task TokensNeverAppearInTheBodyOfTheAnswer()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheVisitorLoginRequest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var textOfTheApiAnswerToCheck = await answerToTheVisitorLoginRequest.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FindCookieWrittenByTheAnswer(answerToTheVisitorLoginRequest, "__Host-walletflow-session")!.Value.ToString(), textOfTheApiAnswerToCheck);
        Assert.DoesNotContain(FindCookieWrittenByTheAnswer(answerToTheVisitorLoginRequest, "__Host-walletflow-device")!.Value.ToString(), textOfTheApiAnswerToCheck);
    }

    // La deuxième connexion du même navigateur garde l'appareil, et renouvelle son cookie.
    [Fact]
    public async Task SecondLoginOfTheSameBrowserKeepsTheDeviceAndRenewsItsCookie()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheFirstLoginRequest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var answerToTheSecondLoginRequest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        Assert.Equal(
            FindCookieWrittenByTheAnswer(answerToTheFirstLoginRequest, "__Host-walletflow-device")!.Value,
            FindCookieWrittenByTheAnswer(answerToTheSecondLoginRequest, "__Host-walletflow-device")!.Value);
        Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedDevices);
    }

    // Se reconnecter dans le même navigateur ferme la session d'avant : une seule reste ouverte.
    [Fact]
    public async Task SecondLoginOfTheSameBrowserClosesTheFirstSession()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // --- Refus

    [Theory]
    [InlineData(EmailAddressOfTheLoginTests, WrongPasswordTypedInTheLoginTests)] // mauvais mot de passe
    [InlineData("inconnu@exemple.test", ValidPasswordChosenInTheseTests)]        // adresse inconnue
    [InlineData("sans-arobase", ValidPasswordChosenInTheseTests)]                // adresse mal formée
    public async Task WrongCredentialsAnswerTheExactGenericMessageWithoutAnyCookie(string emailAddressTypedInThisTest, string passwordTypedByTheVisitorInThisTest)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheVisitorLoginRequest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, emailAddressTypedInThisTest, passwordTypedByTheVisitorInThisTest);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheVisitorLoginRequest, HttpStatusCode.Unauthorized, "Identifiants incorrects");
        Assert.False(answerToTheVisitorLoginRequest.Headers.Contains("Set-Cookie"));
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheVisitorLoginRequest);
    }

    // Lit tout ce qui compte dans une réponse d'erreur : son code, son titre et sa forme.
    private static async Task<string> DescribeErrorAnswerWithoutItsTraceIdentifierAsync(HttpResponseMessage answerOfTheApiToDescribe)
    {
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerOfTheApiToDescribe.Content.ReadAsStringAsync());
        var descriptionOfTheErrorAnswerBeingBuilt = (int)answerOfTheApiToDescribe.StatusCode + "|";

        foreach (var onePropertyOfTheApiAnswer in jsonDocumentOfTheApiAnswer.RootElement.EnumerateObject())
        {
            if (onePropertyOfTheApiAnswer.Name != "traceId")
            {
                descriptionOfTheErrorAnswerBeingBuilt = descriptionOfTheErrorAnswerBeingBuilt + onePropertyOfTheApiAnswer.Name + "=" + onePropertyOfTheApiAnswer.Value + "|";
            }
        }

        return descriptionOfTheErrorAnswerBeingBuilt;
    }

    // Une adresse inconnue, un mauvais mot de passe et un compte bloqué doivent donner exactement la même réponse.
    [Fact]
    public async Task UnknownAddressWrongPasswordAndLockedAccountGiveExactlyTheSameAnswer()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountLockedBeforeThisLoginTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, "compte-bloque@exemple.test");
        accountLockedBeforeThisLoginTest.FailedLoginAttemptCount = 5;
        accountLockedBeforeThisLoginTest.LockoutCount = 1;
        accountLockedBeforeThisLoginTest.LockoutEndsAt = apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddHours(1);
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, "compte-ouvert@exemple.test");
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerGivenForTheUnknownAddress = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "inconnu@exemple.test", ValidPasswordChosenInTheseTests);
        var answerGivenForTheWrongPassword = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "compte-ouvert@exemple.test", WrongPasswordTypedInTheLoginTests);
        var answerGivenForTheLockedAccount = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "compte-bloque@exemple.test", ValidPasswordChosenInTheseTests);

        var descriptionOfTheAnswerForUnknownAddress = await DescribeErrorAnswerWithoutItsTraceIdentifierAsync(answerGivenForTheUnknownAddress);
        Assert.Equal(descriptionOfTheAnswerForUnknownAddress, await DescribeErrorAnswerWithoutItsTraceIdentifierAsync(answerGivenForTheWrongPassword));
        Assert.Equal(descriptionOfTheAnswerForUnknownAddress, await DescribeErrorAnswerWithoutItsTraceIdentifierAsync(answerGivenForTheLockedAccount));
    }

    [Fact]
    public async Task UnconfirmedAccountWithRightPasswordAnswersForbiddenWithTheInvitation()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, emailAddressOfTheAccountIsConfirmed: false);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheVisitorLoginRequest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(
            answerToTheVisitorLoginRequest,
            HttpStatusCode.Forbidden,
            "Ton adresse e-mail n'est pas encore confirmée. Ouvre le lien reçu par e-mail, ou inscris-toi à nouveau pour en recevoir un autre.");
        Assert.False(answerToTheVisitorLoginRequest.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task UnconfirmedAccountWithWrongPasswordGetsTheGenericMessage()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, emailAddressOfTheAccountIsConfirmed: false);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheVisitorLoginRequest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, WrongPasswordTypedInTheLoginTests);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheVisitorLoginRequest, HttpStatusCode.Unauthorized, "Identifiants incorrects");
    }

    [Fact]
    public async Task FiveWrongPasswordsLockTheAccountAndTheRightPasswordIsThenRefused()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisLoginTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        for (var numberOfTheWrongPasswordBeingTried = 1; numberOfTheWrongPasswordBeingTried <= 5; numberOfTheWrongPasswordBeingTried++)
        {
            await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, WrongPasswordTypedInTheLoginTests);
        }

        var answerDuringTheLockoutOfTheAccount = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerDuringTheLockoutOfTheAccount, HttpStatusCode.Unauthorized, "Identifiants incorrects");
        Assert.Equal(1, accountUsedByThisLoginTest.LockoutCount);

        apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(15));
        var answerAfterTheEndOfTheLockout = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        Assert.Equal(HttpStatusCode.OK, answerAfterTheEndOfTheLockout.StatusCode);
    }

    // SS §11 : le journal note l'adresse IP de la requête.
    [Fact]
    public async Task IpAddressOfTheRequestIsWrittenInTheSecurityJournal()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientThatKeepsTheCookiesOfTheBrowser.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.7");

        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, WrongPasswordTypedInTheLoginTests);

        var securityEventOfTheFailedLogin = Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEventOfTheFailedLogin.IpAddress);
    }

    // --- Limite de débit

    [Fact]
    public async Task LoginIsLimitedToTenAttemptsPerMinuteAndPerIpAddress()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientOfTheFirstIpAddress = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientOfTheFirstIpAddress.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.10");
        var clientOfTheSecondIpAddress = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientOfTheSecondIpAddress.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.11");

        for (var numberOfTheAttemptBeingSent = 1; numberOfTheAttemptBeingSent <= 10; numberOfTheAttemptBeingSent++)
        {
            var answerToTheAttemptUnderTheLimit = await SendLoginRequestToTheApiAsync(clientOfTheFirstIpAddress, "inconnu@exemple.test");
            Assert.Equal(HttpStatusCode.Unauthorized, answerToTheAttemptUnderTheLimit.StatusCode);
        }

        var answerToTheAttemptOverTheLimit = await SendLoginRequestToTheApiAsync(clientOfTheFirstIpAddress, "inconnu@exemple.test");
        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheAttemptOverTheLimit, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");

        var answerToTheOtherIpAddress = await SendLoginRequestToTheApiAsync(clientOfTheSecondIpAddress, "inconnu@exemple.test");
        Assert.Equal(HttpStatusCode.Unauthorized, answerToTheOtherIpAddress.StatusCode);
    }

    // Le test ne peut pas attendre : il vérifie la durée exacte de la limite (SS §6).
    [Fact]
    public void LoginLimitLastsExactlyOneMinute()
    {
        var optionsOfTheLoginAttemptsLimit = RateLimitingConfiguration.CreateOptionsOfTheLoginAttemptsLimit();

        Assert.Equal(10, optionsOfTheLoginAttemptsLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), optionsOfTheLoginAttemptsLimit.Window);
    }

    // --- Entrées hostiles

    // Envoie un texte JSON écrit à la main sur la route de connexion.
    private static async Task<HttpResponseMessage> SendRawJsonToTheLoginEndpointAsync(HttpClient clientThatCallsTheApiInMemory, string rawJsonSentByTheTest)
    {
        return await clientThatCallsTheApiInMemory.PostAsync(AddressOfTheAccountLoginEndpoint, new StringContent(rawJsonSentByTheTest, Encoding.UTF8, "application/json"));
    }

    // Aucune de ces valeurs ne doit provoquer d'erreur 500 : le visiteur reçoit le refus normal.
    [Theory]
    [InlineData("""{"email":"utilisateur@exemple.test","password":"Abc￾"}""")]     // caractère que .NET refuse de normaliser
    [InlineData("""{"email":"utilisateur@exemple.test","password":"Abc￿"}""")]     // caractère interdit par Unicode
    [InlineData("""{"email":"utilisateur@exemple.test","password":"Abc\u0000def"}""")]  // caractère nul
    [InlineData("""{"email":"nul\u0000@exemple.test","password":"Mot-De-Passe-Valide-1"}""")] // caractère nul, refusé par PostgreSQL
    [InlineData("""{"email":"utilisateur@exemple.test\n","password":"Abc-Mauvais-1"}""")]     // retour à la ligne
    [InlineData("""{"email":"' OR 1=1 --@exemple.test","password":"' OR 1=1 --"}""")]         // injection SQL
    public async Task HostileValuesGetTheNormalRefusal(string rawJsonSentByTheTest)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestWithHostileValues = await SendRawJsonToTheLoginEndpointAsync(clientThatKeepsTheCookiesOfTheBrowser, rawJsonSentByTheTest);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheRequestWithHostileValues, HttpStatusCode.Unauthorized, "Identifiants incorrects");
    }

    // Le lecteur JSON de .NET refuse une moitié d'emoji : elle n'arrive donc jamais jusqu'aux règles du mot de passe.
    [Theory]
    [InlineData("""{"email":"utilisateur@exemple.test","password":"Abc\ud800"}""")]
    [InlineData("""{"email":"utilisateur@exemple.test","password":"Abc\udc00"}""")]
    [InlineData("""{"email":"utilisateur\ud800@exemple.test","password":"Abc"}""")]
    public async Task HalfOfAnEmojiIsRefusedAsUnreadableRequest(string rawJsonSentByTheTest)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestWithHostileValues = await SendRawJsonToTheLoginEndpointAsync(clientThatKeepsTheCookiesOfTheBrowser, rawJsonSentByTheTest);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheRequestWithHostileValues, HttpStatusCode.BadRequest, ApiErrorConfiguration.TitleOfEveryRefusedRequestAnswer);
        Assert.DoesNotContain("ud800", await answerToTheRequestWithHostileValues.Content.ReadAsStringAsync());
    }

    // Les champs manquants donnent des messages en français, sans nom interne du code.
    [Theory]
    [InlineData("""{"password":"Mot-De-Passe-Valide-1"}""", "Email", "L'adresse e-mail est obligatoire.")]
    [InlineData("""{"email":"utilisateur@exemple.test"}""", "Password", "Le mot de passe est obligatoire.")]
    [InlineData("""{"email":null,"password":"Mot-De-Passe-Valide-1"}""", "Email", "L'adresse e-mail est obligatoire.")]
    [InlineData("""{"email":"utilisateur@exemple.test","password":""}""", "Password", "Le mot de passe est obligatoire.")]
    public async Task MissingFieldIsRefusedWithItsFrenchMessage(string rawJsonSentByTheTest, string nameOfTheFieldInError, string messageExpectedForTheRefusedField)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestOfThisTest = await SendRawJsonToTheLoginEndpointAsync(clientThatKeepsTheCookiesOfTheBrowser, rawJsonSentByTheTest);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestOfThisTest.StatusCode);
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheRequestOfThisTest.Content.ReadAsStringAsync());
        Assert.Equal(messageExpectedForTheRefusedField, jsonDocumentOfTheApiAnswer.RootElement.GetProperty("errors").GetProperty(nameOfTheFieldInError)[0].GetString());
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
    }

    [Theory]
    [InlineData("pas du JSON")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("""{"email":12,"password":34}""")]
    public async Task UnreadableBodyIsRefusedWithoutAnyInternalName(string rawJsonSentByTheTest)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestOfThisTest = await SendRawJsonToTheLoginEndpointAsync(clientThatKeepsTheCookiesOfTheBrowser, rawJsonSentByTheTest);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestOfThisTest.StatusCode);
        var textOfTheApiAnswerToCheck = await answerToTheRequestOfThisTest.Content.ReadAsStringAsync();
        Assert.DoesNotContain(nameof(LoginRequest), textOfTheApiAnswerToCheck);
        Assert.DoesNotContain("requestSentByTheVisitor", textOfTheApiAnswerToCheck);
    }

    // Un site piégé ne peut envoyer qu'un formulaire ou du texte sans demander d'autorisation : ils sont refusés (SS §6).
    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task ContentTypesThatATrappedWebsiteCanSendAreRefused(string contentTypeSentByTheTrappedWebsite)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var bodySentByTheTrappedWebsite = new StringContent("""{"email":"utilisateur@exemple.test","password":"Mot-De-Passe-Valide-1"}""", Encoding.UTF8, contentTypeSentByTheTrappedWebsite);

        var answerGivenToTheTrappedWebsite = await clientThatKeepsTheCookiesOfTheBrowser.PostAsync(AddressOfTheAccountLoginEndpoint, bodySentByTheTrappedWebsite);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, answerGivenToTheTrappedWebsite.StatusCode);
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Le troisième type de formulaire qu'un site piégé peut envoyer sans demander d'autorisation.
    [Fact]
    public async Task MultipartFormOfATrappedWebsiteIsRefused()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var formSentByTheTrappedWebsite = new MultipartFormDataContent
        {
            { new StringContent(EmailAddressOfTheLoginTests), "email" },
            { new StringContent(ValidPasswordChosenInTheseTests), "password" }
        };

        var answerGivenToTheTrappedWebsite = await clientThatKeepsTheCookiesOfTheBrowser.PostAsync(AddressOfTheAccountLoginEndpoint, formSentByTheTrappedWebsite);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, answerGivenToTheTrappedWebsite.StatusCode);
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Seuls l'adresse et le mot de passe sont lus : un champ ajouté ne change rien (SS §4).
    [Fact]
    public async Task ExtraFieldsInTheRequestAreIgnored()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisLoginTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestOfThisTest = await clientThatKeepsTheCookiesOfTheBrowser.PostAsJsonAsync(AddressOfTheAccountLoginEndpoint, new
        {
            email = EmailAddressOfTheLoginTests,
            password = ValidPasswordChosenInTheseTests,
            role = "administrateur",
            userId = Guid.NewGuid(),
            failedLoginAttemptCount = 0
        });

        Assert.Equal(HttpStatusCode.OK, answerToTheRequestOfThisTest.StatusCode);
        Assert.Equal(UserRole.User, accountUsedByThisLoginTest.Role);
        Assert.Equal(accountUsedByThisLoginTest.Id, Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions).UserId);
    }

    // Un cookie de session invalide ne doit pas empêcher de se connecter.
    [Fact]
    public async Task JunkSessionCookieDoesNotPreventTheLogin()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatSendsAJunkCookie = CreateClientThatOnlySendsHandwrittenCookies(apiStartedInMemoryForThisTest, "__Host-walletflow-session=faux; __Host-walletflow-device=faux");

        var answerToTheVisitorLoginRequest = await SendLoginRequestToTheApiAsync(clientThatSendsAJunkCookie);

        Assert.Equal(HttpStatusCode.OK, answerToTheVisitorLoginRequest.StatusCode);
        Assert.NotEqual("faux", FindCookieWrittenByTheAnswer(answerToTheVisitorLoginRequest, "__Host-walletflow-device")!.Value.ToString());
    }

    // --- Journal technique

    // Au niveau le plus détaillé, le journal technique ne contient jamais de secret (SS §7).
    [Fact]
    public async Task TechnicalLogNeverContainsTheSecretsOfTheLogin()
    {
        using var apiWithTheMostDetailedLog = new ApiFactoryLoggingEverything();
        AddAccountToTheFakeRepositoryOfTheApi(apiWithTheMostDetailedLog);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiWithTheMostDetailedLog);

        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, WrongPasswordTypedInTheLoginTests);
        var answerToTheVisitorLoginRequest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var csrfTokenOfTheLoggedInUser = await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatKeepsTheCookiesOfTheBrowser);
        await SendLogoutRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);

        var textOfTheWholeTechnicalLog = string.Join("\n", apiWithTheMostDetailedLog.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog);
        Assert.NotEmpty(textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(EmailAddressOfTheLoginTests, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(ValidPasswordChosenInTheseTests, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(WrongPasswordTypedInTheLoginTests, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(FindCookieWrittenByTheAnswer(answerToTheVisitorLoginRequest, "__Host-walletflow-session")!.Value.ToString(), textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(FindCookieWrittenByTheAnswer(answerToTheVisitorLoginRequest, "__Host-walletflow-device")!.Value.ToString(), textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(csrfTokenOfTheLoggedInUser, textOfTheWholeTechnicalLog);
    }
}
