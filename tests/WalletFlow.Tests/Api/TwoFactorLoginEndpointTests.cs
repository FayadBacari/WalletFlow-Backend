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
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheLoginRequestWithThePassword = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        Assert.Equal(HttpStatusCode.OK, answerToTheLoginRequestWithThePassword.StatusCode);
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheLoginRequestWithThePassword.Content.ReadAsStringAsync());
        Assert.True(jsonDocumentOfTheApiAnswer.RootElement.GetProperty("isTwoFactorCodeRequired").GetBoolean());
        Assert.Equal(AccountLoginController.MessageSentWhenTheLoginWaitsForItsCode, jsonDocumentOfTheApiAnswer.RootElement.GetProperty("message").GetString());
        Assert.Null(FindCookieWrittenByTheAnswer(answerToTheLoginRequestWithThePassword, SessionCookiesConfiguration.NameOfTheUserSessionCookie));
        Assert.Null(FindCookieWrittenByTheAnswer(answerToTheLoginRequestWithThePassword, SessionCookiesConfiguration.NameOfTheDeviceTokenCookie));
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheLoginRequestWithThePassword);
    }

    // SS §3 : ce cookie n'est lisible par aucun script, ne part qu'en HTTPS, et ne vaut que pour le site lui-même.
    [Fact]
    public async Task WaitingLoginCookieHasItsExactAttributesAndNeverAppearsInTheBody()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheLoginRequestWithThePassword = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var waitingLoginCookieWrittenByTheAnswer = FindCookieWrittenByTheAnswer(answerToTheLoginRequestWithThePassword, NameOfTheWaitingLoginCookieInTheseTests);
        Assert.NotNull(waitingLoginCookieWrittenByTheAnswer);
        Assert.True(waitingLoginCookieWrittenByTheAnswer.HttpOnly);
        Assert.True(waitingLoginCookieWrittenByTheAnswer.Secure);
        Assert.Equal(SameSiteMode.Strict, waitingLoginCookieWrittenByTheAnswer.SameSite);
        Assert.Equal("/", waitingLoginCookieWrittenByTheAnswer.Path.ToString());
        Assert.Equal(0, waitingLoginCookieWrittenByTheAnswer.Domain.Length);
        Assert.Null(waitingLoginCookieWrittenByTheAnswer.MaxAge);
        Assert.Null(waitingLoginCookieWrittenByTheAnswer.Expires);
        Assert.DoesNotContain(waitingLoginCookieWrittenByTheAnswer.Value.ToString(), await answerToTheLoginRequestWithThePassword.Content.ReadAsStringAsync());
    }

    // Le contrat avec le site : une connexion sans double authentification ne demande aucun code.
    [Fact]
    public async Task AccountWithoutTwoFactorIsToldThatNoCodeIsRequired()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheLoginRequestWithThePassword = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheLoginRequestWithThePassword.Content.ReadAsStringAsync());
        Assert.False(jsonDocumentOfTheApiAnswer.RootElement.GetProperty("isTwoFactorCodeRequired").GetBoolean());
        Assert.Null(FindCookieWrittenByTheAnswer(answerToTheLoginRequestWithThePassword, NameOfTheWaitingLoginCookieInTheseTests));
    }

    // Le cookie d'attente n'ouvre aucune porte : il ne vaut pas une session.
    [Fact]
    public async Task WaitingLoginCookieAloneGivesAccessToNothing()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerGivenByTheProtectedRouteOfTheApi = await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, answerGivenByTheProtectedRouteOfTheApi.StatusCode);
    }

    // Copié dans le cookie de session, le jeton d'attente n'est reconnu par aucune session.
    [Fact]
    public async Task WaitingLoginTokenCopiedIntoTheSessionCookieGivesAccessToNothing()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var answerToTheLoginRequestWithThePassword = await SendLoginRequestToTheApiAsync(CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest));
        var waitingLoginTokenOfTheVisitor = FindCookieWrittenByTheAnswer(answerToTheLoginRequestWithThePassword, NameOfTheWaitingLoginCookieInTheseTests)!.Value.ToString();
        var clientUsedByTheAttackerOfThisTest = CreateClientThatOnlySendsHandwrittenCookies(apiStartedInMemoryForThisTest, SessionCookiesConfiguration.NameOfTheUserSessionCookie + "=" + waitingLoginTokenOfTheVisitor);

        var answerGivenByTheProtectedRouteOfTheApi = await clientUsedByTheAttackerOfThisTest.GetAsync(AddressOfTheCsrfTokenEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, answerGivenByTheProtectedRouteOfTheApi.StatusCode);
    }

    // --- Le bon code

    [Fact]
    public async Task RightCodeOpensTheSessionWritesItsCookiesAndDeletesTheWaitingCookie()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheRequestCarryingTheCode = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.Equal(HttpStatusCode.OK, answerToTheRequestCarryingTheCode.StatusCode);
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheRequestCarryingTheCode.Content.ReadAsStringAsync());
        Assert.False(jsonDocumentOfTheApiAnswer.RootElement.GetProperty("isTwoFactorCodeRequired").GetBoolean());
        Assert.Equal(AccountLoginController.MessageSentWhenTheVisitorIsLoggedIn, jsonDocumentOfTheApiAnswer.RootElement.GetProperty("message").GetString());

        var sessionCookieWrittenByTheAnswer = FindCookieWrittenByTheAnswer(answerToTheRequestCarryingTheCode, SessionCookiesConfiguration.NameOfTheUserSessionCookie);
        Assert.NotNull(sessionCookieWrittenByTheAnswer);
        Assert.True(sessionCookieWrittenByTheAnswer.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, sessionCookieWrittenByTheAnswer.SameSite);
        Assert.Equal(TimeSpan.FromDays(365), FindCookieWrittenByTheAnswer(answerToTheRequestCarryingTheCode, SessionCookiesConfiguration.NameOfTheDeviceTokenCookie)!.MaxAge);

        // Un navigateur préfère Max-Age à une date passée : l'en-tête de suppression ne doit donc pas en contenir.
        var waitingLoginCookieDeletedByTheAnswer = FindCookieWrittenByTheAnswer(answerToTheRequestCarryingTheCode, NameOfTheWaitingLoginCookieInTheseTests);
        Assert.NotNull(waitingLoginCookieDeletedByTheAnswer);
        Assert.Null(waitingLoginCookieDeletedByTheAnswer.MaxAge);
        Assert.True(waitingLoginCookieDeletedByTheAnswer.Expires < DateTimeOffset.UtcNow);
        Assert.Equal(0, waitingLoginCookieDeletedByTheAnswer.Value.Length);

        var answerGivenByTheProtectedRouteOfTheApi = await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint);
        Assert.Equal(HttpStatusCode.OK, answerGivenByTheProtectedRouteOfTheApi.StatusCode);
    }

    [Fact]
    public async Task BackupCodeWithItsDashAlsoOpensTheSession()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheRequestCarryingTheCode = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, activatedAccountUsedByThisTest.BackupCodeInItsCleanForm.Insert(5, "-"));

        Assert.Equal(HttpStatusCode.OK, answerToTheRequestCarryingTheCode.StatusCode);
        Assert.NotNull(apiStartedInMemoryForThisTest.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes[0].UsedAt);
    }

    // Le cookie d'appareil du navigateur est lu au moment du code : un appareil déjà connu du compte n'en crée pas d'autre.
    [Fact]
    public async Task KnownDeviceDoesNotCreateANewDeviceAtTheEndOfTheLogin()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await LogInWithPasswordAndCodeThenAskForTheCsrfTokenAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);
        WaitForTheNextCodeOnThePhone(apiStartedInMemoryForThisTest);

        await LogInWithPasswordAndCodeThenAskForTheCsrfTokenAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);

        Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedDevices);
        Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // SS §2 : dans la vraie API aussi, la mémoire des codes utilisés survit d'une requête à l'autre.
    [Fact]
    public async Task CodeAlreadyUsedIsRefusedAtTheNextLoginThroughTheApi()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var codeShownOnThePhoneInThisTest = CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var answerToTheFirstUseOfTheCode = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, codeShownOnThePhoneInThisTest);
        Assert.Equal(HttpStatusCode.OK, answerToTheFirstUseOfTheCode.StatusCode);

        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var answerToTheSecondUseOfTheCode = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, codeShownOnThePhoneInThisTest);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheSecondUseOfTheCode.StatusCode);
    }

    // SS §11 : le journal note l'adresse IP de la requête qui donne le code, pour un refus comme pour une réussite.
    [Fact]
    public async Task JournalKeepsTheIpAddressOfTheRequestThatGivesTheCode()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientThatKeepsTheCookiesOfTheBrowser.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.7");
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, FindCodeThatIsWrongNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));
        await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        var securityEventsSavedByTheseRequests = apiStartedInMemoryForThisTest.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents;
        Assert.Equal(2, securityEventsSavedByTheseRequests.Count);
        foreach (var securityEventSavedByOneRequest in securityEventsSavedByTheseRequests)
        {
            Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEventSavedByOneRequest.IpAddress);
        }
    }

    // --- Les refus

    // 400 avec le champ Code : le site reste sur le formulaire du code, et le cookie d'attente ne bouge pas.
    [Fact]
    public async Task WrongCodeAnswersWithAFieldErrorAndKeepsTheWaitingCookie()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheWrongCodeOfTheVisitor = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, FindCodeThatIsWrongNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheWrongCodeOfTheVisitor.StatusCode);
        Assert.Equal("application/problem+json", answerToTheWrongCodeOfTheVisitor.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, await ReadErrorMessageOfThisRequestFieldAsync(answerToTheWrongCodeOfTheVisitor, "Code"));
        Assert.Null(FindCookieWrittenByTheAnswer(answerToTheWrongCodeOfTheVisitor, NameOfTheWaitingLoginCookieInTheseTests));
        Assert.Null(FindCookieWrittenByTheAnswer(answerToTheWrongCodeOfTheVisitor, SessionCookiesConfiguration.NameOfTheUserSessionCookie));
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheWrongCodeOfTheVisitor);

        var answerToTheRightCodeOfTheVisitor = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));
        Assert.Equal(HttpStatusCode.OK, answerToTheRightCodeOfTheVisitor.StatusCode);
    }

    // 401 : le site revient au formulaire du mot de passe, et le cookie d'attente disparaît.
    [Fact]
    public async Task FifthWrongCodeAnswers401AndDeletesTheWaitingCookie()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var codeThatIsWrongInThisTest = FindCodeThatIsWrongNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);

        for (var numberOfTheWrongCodeBeingSent = 1; numberOfTheWrongCodeBeingSent <= 4; numberOfTheWrongCodeBeingSent++)
        {
            var answerToOneWrongCodeOfTheVisitor = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, codeThatIsWrongInThisTest);
            Assert.Equal(HttpStatusCode.BadRequest, answerToOneWrongCodeOfTheVisitor.StatusCode);
        }

        var answerToTheFifthWrongCode = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, codeThatIsWrongInThisTest);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheFifthWrongCode, HttpStatusCode.Unauthorized, TwoFactorLoginService.ErrorMessageForLoginThatMustRestart);
        Assert.True(FindCookieWrittenByTheAnswer(answerToTheFifthWrongCode, NameOfTheWaitingLoginCookieInTheseTests)!.Expires < DateTimeOffset.UtcNow);
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    [Fact]
    public async Task CodeWithoutAnyWaitingLoginAnswers401()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestCarryingTheCode = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "123456");

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheRequestCarryingTheCode, HttpStatusCode.Unauthorized, TwoFactorLoginService.ErrorMessageForLoginThatMustRestart);
    }

    [Fact]
    public async Task ForgedWaitingLoginCookieAnswers401AndIsNotCounted()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientUsedByTheAttackerOfThisTest = CreateClientThatOnlySendsHandwrittenCookies(apiStartedInMemoryForThisTest, NameOfTheWaitingLoginCookieInTheseTests + "=jeton-invente");

        var answerToTheRequestCarryingTheCode = await SendTwoFactorLoginRequestToTheApiAsync(clientUsedByTheAttackerOfThisTest, "123456");

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheRequestCarryingTheCode, HttpStatusCode.Unauthorized, TwoFactorLoginService.ErrorMessageForLoginThatMustRestart);
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
    }

    [Fact]
    public async Task ExpiredWaitingLoginAnswers401EvenWithTheRightCode()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(5));
        var answerToTheRequestCarryingTheCode = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheRequestCarryingTheCode, HttpStatusCode.Unauthorized, TwoFactorLoginService.ErrorMessageForLoginThatMustRestart);
    }

    // SS §3 : une récupération pendant l'attente annule cette connexion. Le voleur du mot de passe ne garde rien.
    [Fact]
    public async Task PasswordRecoveryDuringTheWaitCancelsTheWaitingLogin()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientUsedByTheThiefOfThePassword = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var clientUsedByTheOwnerOfTheAccount = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientUsedByTheThiefOfThePassword);

        var tokenReadInTheRecoveryLinkOfTheEmail = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientUsedByTheOwnerOfTheAccount, EmailAddressOfTheLoginTests);
        var answerToTheRecoveryOfThePassword = await SendResetPasswordRequestToTheApiAsync(clientUsedByTheOwnerOfTheAccount, tokenReadInTheRecoveryLinkOfTheEmail);
        Assert.Equal(HttpStatusCode.OK, answerToTheRecoveryOfThePassword.StatusCode);

        var answerToTheCodeOfTheThief = await SendTwoFactorLoginRequestToTheApiAsync(clientUsedByTheThiefOfThePassword, CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheCodeOfTheThief, HttpStatusCode.Unauthorized, TwoFactorLoginService.ErrorMessageForLoginThatMustRestart);
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // GR Auth : la récupération n'enlève pas le code. Une boîte mail piratée ne suffit donc pas à entrer.
    [Fact]
    public async Task AfterAPasswordRecoveryTheLoginStillAsksForTheCode()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheRecoveryLinkOfTheEmail = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);
        await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLinkOfTheEmail);

        var answerToTheLoginRequestWithThePassword = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, NewPasswordChosenInTheRecoveryTests);

        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheLoginRequestWithThePassword.Content.ReadAsStringAsync());
        Assert.True(jsonDocumentOfTheApiAnswer.RootElement.GetProperty("isTwoFactorCodeRequired").GetBoolean());
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // --- Les requêtes mal formées

    [Fact]
    public async Task MissingCodeAnswersWithTheStableFieldError()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestCarryingTheCode = await clientThatKeepsTheCookiesOfTheBrowser.PostAsJsonAsync(AddressOfTheTwoFactorLoginEndpoint, new { });

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestCarryingTheCode.StatusCode);
        Assert.Equal("Le code est obligatoire.", await ReadErrorMessageOfThisRequestFieldAsync(answerToTheRequestCarryingTheCode, "Code"));
    }

    [Theory]
    [InlineData("{\"code\": 123456}")]
    [InlineData("{\"code\": [\"123456\"]}")]
    [InlineData("{\"code\": null}")]
    [InlineData("{\"code\": \"\\uD800\"}")]
    [InlineData("pas du json")]
    [InlineData("")]
    public async Task UnreadableBodyAnswers400WithoutRecopyingWhatWasSent(string bodyOfTheRequestSentInThisTest)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestCarryingTheCode = await clientThatKeepsTheCookiesOfTheBrowser.PostAsync(AddressOfTheTwoFactorLoginEndpoint, new StringContent(bodyOfTheRequestSentInThisTest, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestCarryingTheCode.StatusCode);
        Assert.DoesNotContain("123456", await answerToTheRequestCarryingTheCode.Content.ReadAsStringAsync());
    }

    // SS §6 : cette route n'accepte que du JSON, qu'un site piégé ne peut pas envoyer sans autorisation.
    [Fact]
    public async Task FormSentByATrappedWebsiteIsRefused()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerGivenToTheTrappedWebsite = await clientThatKeepsTheCookiesOfTheBrowser.PostAsync(AddressOfTheTwoFactorLoginEndpoint, new FormUrlEncodedContent(new Dictionary<string, string> { { "code", "123456" } }));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, answerGivenToTheTrappedWebsite.StatusCode);
    }

    [Fact]
    public async Task GetRequestOnTheRouteOfTheCodeIsNotAllowed()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestSentWithGet = await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheTwoFactorLoginEndpoint);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, answerToTheRequestSentWithGet.StatusCode);
    }

    // --- La limite de débit

    // SS §6 : 10 essais par minute et par adresse IP. Cette route a son propre compteur, distinct de celui du mot de passe.
    [Fact]
    public async Task EleventhAttemptOfTheMinuteFromOneAddressIsRefusedAndOtherAddressesStillPass()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientOfTheFirstVisitorIpAddress = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientOfTheFirstVisitorIpAddress.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.10");
        var clientOfTheSecondVisitorIpAddress = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientOfTheSecondVisitorIpAddress.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.11");

        for (var numberOfTheAttemptBeingSent = 1; numberOfTheAttemptBeingSent <= 10; numberOfTheAttemptBeingSent++)
        {
            var answerToOneCodeAttemptUnderTheLimit = await SendTwoFactorLoginRequestToTheApiAsync(clientOfTheFirstVisitorIpAddress, "123456");
            Assert.Equal(HttpStatusCode.Unauthorized, answerToOneCodeAttemptUnderTheLimit.StatusCode);
        }

        var answerToTheAttemptOverTheLimit = await SendTwoFactorLoginRequestToTheApiAsync(clientOfTheFirstVisitorIpAddress, "123456");
        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheAttemptOverTheLimit, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");

        var answerGivenToRequestFromTheOtherAddress = await SendTwoFactorLoginRequestToTheApiAsync(clientOfTheSecondVisitorIpAddress, "123456");
        Assert.Equal(HttpStatusCode.Unauthorized, answerGivenToRequestFromTheOtherAddress.StatusCode);

        var answerGivenByTheRouteOfThePassword = await SendLoginRequestToTheApiAsync(clientOfTheFirstVisitorIpAddress, "adresse-inconnue@exemple.test");
        Assert.Equal(HttpStatusCode.Unauthorized, answerGivenByTheRouteOfThePassword.StatusCode);
    }

    [Fact]
    public void LimitOfTheCodeAttemptsIsWrittenWithItsExactValues()
    {
        var optionsOfTheCodeAttemptsLimit = RateLimitingConfiguration.CreateOptionsOfTheTwoFactorCodeAttemptsLimit();

        Assert.Equal(10, optionsOfTheCodeAttemptsLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), optionsOfTheCodeAttemptsLimit.Window);
    }

    // --- Les pannes, vues du visiteur

    // Une panne de la base répond 500, sans détail, et n'ouvre rien (SS §10).
    [Fact]
    public async Task DatabaseFailureAnswersAGenericErrorInProduction()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedLikeInProductionForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedLikeInProductionForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        apiStartedLikeInProductionForThisTest.RepositoryThatKeepsSessionsInMemory.DatabaseIsDownDuringThisTest = true;

        var answerGivenDuringTheSimulatedFailure = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, CalculateCodeShownOnThePhoneNow(apiStartedLikeInProductionForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerGivenDuringTheSimulatedFailure, HttpStatusCode.InternalServerError, "Une erreur est survenue. Réessaie plus tard.");
        var textOfTheAnswerDuringTheFailure = await answerGivenDuringTheSimulatedFailure.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeUserSessionRepository.TechnicalDetailOfTheSimulatedSessionFailure, textOfTheAnswerDuringTheFailure);
        Assert.Null(FindCookieWrittenByTheAnswer(answerGivenDuringTheSimulatedFailure, SessionCookiesConfiguration.NameOfTheUserSessionCookie));
    }

    // Une clé perdue ne se voit pas comme un mauvais code : le visiteur reçoit 500, sans détail, et la panne va dans le journal technique.
    [Fact]
    public async Task LostEncryptionKeyAnswersAGenericErrorAndIsWrittenInTheTechnicalLog()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedLikeInProductionForThisTest);
        activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateNewEncryptionKeyForOneTest()).EncryptSecretOfThisUserAccount(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication, activatedAccountUsedByThisTest.UserAccount.Id);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedLikeInProductionForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheRequestCarryingTheCode = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, CalculateCodeShownOnThePhoneNow(apiStartedLikeInProductionForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheRequestCarryingTheCode, HttpStatusCode.InternalServerError, "Une erreur est survenue. Réessaie plus tard.");
        var textOfTheWholeTechnicalLog = string.Join("\n", apiStartedLikeInProductionForThisTest.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog);
        Assert.Contains("AuthenticationTagMismatchException", textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication, textOfTheWholeTechnicalLog);
    }

    // --- Le journal technique

    // Au niveau le plus détaillé, le journal technique ne contient jamais de code ni de jeton (SS §7).
    [Fact]
    public async Task TechnicalLogNeverContainsTheCodesOrTheTokensOfTheLogin()
    {
        using var apiWithTheMostDetailedLog = new ApiFactoryLoggingEverything();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiWithTheMostDetailedLog);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiWithTheMostDetailedLog);
        var codeThatIsWrongInThisTest = FindCodeThatIsWrongNow(apiWithTheMostDetailedLog, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);
        var codeShownOnThePhoneInThisTest = CalculateCodeShownOnThePhoneNow(apiWithTheMostDetailedLog, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);

        var answerToTheLoginRequestWithThePassword = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, codeThatIsWrongInThisTest);
        var answerToTheRightCodeOfTheVisitor = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, codeShownOnThePhoneInThisTest);

        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, activatedAccountUsedByThisTest.BackupCodeInItsCleanForm);

        var textOfTheWholeTechnicalLog = string.Join("\n", apiWithTheMostDetailedLog.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog);
        Assert.NotEmpty(textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(codeThatIsWrongInThisTest, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(codeShownOnThePhoneInThisTest, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(activatedAccountUsedByThisTest.BackupCodeInItsCleanForm, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(FindCookieWrittenByTheAnswer(answerToTheLoginRequestWithThePassword, NameOfTheWaitingLoginCookieInTheseTests)!.Value.ToString(), textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(FindCookieWrittenByTheAnswer(answerToTheRightCodeOfTheVisitor, SessionCookiesConfiguration.NameOfTheUserSessionCookie)!.Value.ToString(), textOfTheWholeTechnicalLog);
    }
}
