using System.Net;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Controllers;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie l'e-mail et le lien de déblocage de bout en bout, avec le vrai Argon2, les vrais jetons et la vraie file (GR Auth, SS §2).
public class AccountUnlockEndpointTests
{
    // Fait les 5 mauvais essais qui bloquent le compte, attend l'e-mail, et renvoie le jeton de son lien.
    private static async Task<string> LockTheAccountThenReadTheTokenOfTheUnlockLinkAsync(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest, HttpClient clientThatCallsTheApiInMemory)
    {
        var numberOfEmailsBeforeTheLockout = apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Count;

        for (var numberOfTheWrongPasswordBeingTried = 1; numberOfTheWrongPasswordBeingTried <= 5; numberOfTheWrongPasswordBeingTried++)
        {
            await SendLoginRequestToTheApiAsync(clientThatCallsTheApiInMemory, EmailAddressOfTheLoginTests, WrongPasswordTypedInTheLoginTests);
        }

        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Count == numberOfEmailsBeforeTheLockout + 1);

        return ReadTokenWrittenInTheConfirmationLink(apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Last().Body);
    }

    [Fact]
    public async Task FifthWrongPasswordSendsTheUnlockEmailInTheBackground()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        await LockTheAccountThenReadTheTokenOfTheUnlockLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser);

        var emailReceivedByTheAddressOwner = Assert.Single(apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Equal(EmailAddressOfTheLoginTests, emailReceivedByTheAddressOwner.RecipientAddress);
        Assert.Equal("Ton compte WalletFlow est bloqué", emailReceivedByTheAddressOwner.Subject);
        Assert.Contains("/unlock-account#token=", emailReceivedByTheAddressOwner.Body);
        Assert.Contains("pendant 15 minutes", emailReceivedByTheAddressOwner.Body);
    }

    // GR Auth : le lien débloque le compte, mais ne connecte pas l'utilisateur.
    [Fact]
    public async Task UnlockLinkOpensTheAccountWithoutLoggingIn()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisLoginTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheUnlockLink = await LockTheAccountThenReadTheTokenOfTheUnlockLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser)).StatusCode);

        var answerToTheAccountUnlockRequest = await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheUnlockLink);

        Assert.Equal(HttpStatusCode.OK, answerToTheAccountUnlockRequest.StatusCode);
        Assert.Equal(AccountUnlockController.MessageSentWhenTheAccountIsUnlocked, JsonDocument.Parse(await answerToTheAccountUnlockRequest.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());
        Assert.False(answerToTheAccountUnlockRequest.Headers.Contains("Set-Cookie"));
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Equal(1, accountUsedByThisLoginTest.LockoutCount);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheAccountUnlockRequest);

        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser)).StatusCode);
    }

    // SS §2 : un seul e-mail par blocage, pour qu'un attaquant ne puisse pas inonder la boîte de la victime.
    [Fact]
    public async Task AttemptsDuringTheLockoutSendNoOtherEmail()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await LockTheAccountThenReadTheTokenOfTheUnlockLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser);

        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, WrongPasswordTypedInTheLoginTests);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        // On envoie une dernière demande qui passe par la même file : quand son e-mail arrive, tout ce qui la précède a été traité.
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, "temoin@exemple.test");
        await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, "temoin@exemple.test");

        Assert.Equal(1, apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Count(emailKeptInMemoryByTheFakeSender => emailKeptInMemoryByTheFakeSender.RecipientAddress == EmailAddressOfTheLoginTests));
    }

    [Fact]
    public async Task UnlockLinkCanOnlyBeUsedOnce()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheUnlockLink = await LockTheAccountThenReadTheTokenOfTheUnlockLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser);
        await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheUnlockLink);

        var answerToTheSecondClickOnTheLink = await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheUnlockLink);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheSecondClickOnTheLink.StatusCode);
    }

    [Fact]
    public async Task UnknownLinkIsRefusedOnItsFieldWithTheExactMessage()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheUnknownLinkOfThisTest = await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "jeton-invente");

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheUnknownLinkOfThisTest.StatusCode);
        Assert.Equal("Ce lien n'est plus valable. Si ton compte est encore bloqué, utilise « Mot de passe oublié ».", await ReadErrorMessageOfThisRequestFieldAsync(answerToTheUnknownLinkOfThisTest, "Token"));
    }

    // Un lien ne sert jamais à un autre usage : un lien de déblocage ne doit jamais changer un mot de passe.
    [Fact]
    public async Task UnlockLinkIsRefusedOnThePasswordResetRoute()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheUnlockLink = await LockTheAccountThenReadTheTokenOfTheUnlockLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser);

        var answerOnThePasswordResetRoute = await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheUnlockLink);

        Assert.Equal(HttpStatusCode.BadRequest, answerOnThePasswordResetRoute.StatusCode);
    }

    [Fact]
    public async Task RecoveryLinkIsRefusedOnTheUnlockRoute()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        var answerGivenOnTheUnlockRoute = await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink);

        Assert.Equal(HttpStatusCode.BadRequest, answerGivenOnTheUnlockRoute.StatusCode);
    }

    // Un nouveau blocage envoie un nouveau lien : l'ancien ne marche plus (SS §2).
    [Fact]
    public async Task NewLockoutReplacesTheOldUnlockLink()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenOfTheFirstUnlockLink = await LockTheAccountThenReadTheTokenOfTheUnlockLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser);

        apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(15));
        var tokenOfTheSecondUnlockLink = await LockTheAccountThenReadTheTokenOfTheUnlockLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser);

        Assert.Contains("pendant 30 minutes", apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Last().Body);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenOfTheFirstUnlockLink)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenOfTheSecondUnlockLink)).StatusCode);
    }

    // La panne de l'e-mail ne change pas la réponse de la connexion : elle révélerait que le compte existe (SS §2).
    [Fact]
    public async Task EmailFailureDoesNotChangeTheLoginAnswerAndIsWrittenInTheTechnicalLog()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        for (var numberOfTheWrongPasswordBeingTried = 1; numberOfTheWrongPasswordBeingTried <= 5; numberOfTheWrongPasswordBeingTried++)
        {
            var answerToTheWrongPasswordOfThisTest = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, WrongPasswordTypedInTheLoginTests);
            await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheWrongPasswordOfThisTest, HttpStatusCode.Unauthorized, "Identifiants incorrects");
        }

        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedInMemoryForThisTest.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog.Any(messageWrittenInTheTechnicalLog =>
            messageWrittenInTheTechnicalLog.StartsWith("Error") && messageWrittenInTheTechnicalLog.Contains("n'a pas pu partir")));
    }

    [Fact]
    public async Task UnlockIsLimitedToTenAttemptsPerHourAndPerIpAddress()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        for (var numberOfTheAttemptBeingSent = 1; numberOfTheAttemptBeingSent <= 10; numberOfTheAttemptBeingSent++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "jeton-invente")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "jeton-invente")).StatusCode);
    }

    // Aucune de ces valeurs ne doit provoquer d'erreur 500 ni apparaître dans la réponse.
    [Theory]
    [InlineData("""{"token":"jeton\ud800"}""")]
    [InlineData("""{"token":"nul\u0000"}""")]
    [InlineData("""{"token":"' OR 1=1 --"}""")]
    [InlineData("""{"token":null}""")]
    [InlineData("""{"token":12}""")]
    [InlineData("""{}""")]
    [InlineData("[]")]
    [InlineData("pas du JSON")]
    public async Task HostileUnlockRequestIsRefusedWithoutAnyErrorOrInternalName(string rawJsonSentByTheTest)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestWithHostileValues = await clientThatKeepsTheCookiesOfTheBrowser.PostAsync(
            AddressOfTheAccountUnlockEndpoint,
            new StringContent(rawJsonSentByTheTest, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestWithHostileValues.StatusCode);
        var textOfTheApiAnswerToCheck = await answerToTheRequestWithHostileValues.Content.ReadAsStringAsync();
        Assert.DoesNotContain("requestSentByTheVisitor", textOfTheApiAnswerToCheck);
        Assert.DoesNotContain("UnlockAccountRequest", textOfTheApiAnswerToCheck);
    }

    [Fact]
    public async Task MissingTokenIsRefusedWithItsFrenchMessage()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestOfThisTest = await clientThatKeepsTheCookiesOfTheBrowser.PostAsync(AddressOfTheAccountUnlockEndpoint, new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal("Le lien de déblocage est obligatoire.", await ReadErrorMessageOfThisRequestFieldAsync(answerToTheRequestOfThisTest, "Token"));
    }

    [Fact]
    public async Task SuccessfulUnlockIsWrittenInTheSecurityJournalWithTheIpAddress()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisLoginTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientThatKeepsTheCookiesOfTheBrowser.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.7");
        var tokenReadInTheUnlockLink = await LockTheAccountThenReadTheTokenOfTheUnlockLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser);

        await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheUnlockLink);

        var securityEventOfTheAccountUnlock = Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks);
        Assert.Equal(SecurityEventType.Unlock, securityEventOfTheAccountUnlock.Type);
        Assert.Equal(accountUsedByThisLoginTest.Id, securityEventOfTheAccountUnlock.UserId);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEventOfTheAccountUnlock.IpAddress);
    }

    // Au niveau le plus détaillé, le journal technique ne contient jamais de secret (SS §7).
    [Fact]
    public async Task TechnicalLogNeverContainsTheSecretsOfTheUnlock()
    {
        using var apiWithTheMostDetailedLog = new ApiFactoryLoggingEverything();
        AddAccountToTheFakeRepositoryOfTheApi(apiWithTheMostDetailedLog);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiWithTheMostDetailedLog);
        var tokenReadInTheUnlockLink = await LockTheAccountThenReadTheTokenOfTheUnlockLinkAsync(apiWithTheMostDetailedLog, clientThatKeepsTheCookiesOfTheBrowser);

        await SendAccountUnlockRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheUnlockLink);

        var textOfTheWholeTechnicalLog = string.Join("\n", apiWithTheMostDetailedLog.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog);
        Assert.NotEmpty(textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(EmailAddressOfTheLoginTests, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(tokenReadInTheUnlockLink, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(WrongPasswordTypedInTheLoginTests, textOfTheWholeTechnicalLog);
    }
}
