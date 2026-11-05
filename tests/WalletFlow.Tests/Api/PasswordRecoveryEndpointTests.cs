using System.Net;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la récupération du mot de passe de bout en bout, avec le vrai Argon2, les vrais jetons et la vraie file (GR Auth, SS §2, §3 et §6).
public class PasswordRecoveryEndpointTests
{
    private const string MessageExpectedForEveryRecoveryRequest = "Si un compte existe avec cette adresse, un e-mail vient d'être envoyé.";

    // --- Demande

    [Fact]
    public async Task ForgotPasswordAnswersTheExactMessageAndSendsTheLinkInTheBackground()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToThePasswordRecoveryRequest = await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        Assert.Equal(HttpStatusCode.OK, answerToThePasswordRecoveryRequest.StatusCode);
        Assert.Equal(MessageExpectedForEveryRecoveryRequest, JsonDocument.Parse(await answerToThePasswordRecoveryRequest.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToThePasswordRecoveryRequest);

        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Count == 1);
        var emailReceivedByTheAddressOwner = apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails[0];
        Assert.Equal(EmailAddressOfTheLoginTests, emailReceivedByTheAddressOwner.RecipientAddress);
        Assert.Contains("/reset-password#token=", emailReceivedByTheAddressOwner.Body);
    }

    // Lit le code et le texte exact d'une réponse.
    private static async Task<string> DescribeTheWholeAnswerOfTheApiAsync(HttpResponseMessage answerOfTheApiToDescribe)
    {
        return (int)answerOfTheApiToDescribe.StatusCode + "|" + await answerOfTheApiToDescribe.Content.ReadAsStringAsync();
    }

    // GR Auth : la page affiche le même message, que l'adresse existe ou non.
    [Fact]
    public async Task AnswerIsExactlyTheSameForUnknownUnconfirmedAndConfirmedAddresses()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, "compte-confirme@exemple.test");
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, "compte-non-confirme@exemple.test", emailAddressOfTheAccountIsConfirmed: false);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerGivenForTheConfirmedAddress = await DescribeTheWholeAnswerOfTheApiAsync(await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "compte-confirme@exemple.test"));
        var answerGivenForTheUnconfirmedAddress = await DescribeTheWholeAnswerOfTheApiAsync(await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "compte-non-confirme@exemple.test"));
        var answerGivenForTheUnknownAddress = await DescribeTheWholeAnswerOfTheApiAsync(await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "inconnu@exemple.test"));

        Assert.Equal(answerGivenForTheConfirmedAddress, answerGivenForTheUnconfirmedAddress);
        Assert.Equal(answerGivenForTheConfirmedAddress, answerGivenForTheUnknownAddress);
    }

    // La route ne touche jamais à la base : même une panne de la base ne change pas sa réponse.
    [Fact]
    public async Task AnswerStaysTheSameEvenDuringADatabaseFailure()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        apiStartedLikeInProductionForThisTest.RepositoryThatKeepsAccountsInMemory.DatabaseIsDownDuringThisTest = true;
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedLikeInProductionForThisTest);

        var answerDuringTheSimulatedDatabaseFailure = await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        Assert.Equal(HttpStatusCode.OK, answerDuringTheSimulatedDatabaseFailure.StatusCode);
        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedLikeInProductionForThisTest.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog.Any(messageWrittenInTheTechnicalLog =>
            messageWrittenInTheTechnicalLog.StartsWith("Error") && messageWrittenInTheTechnicalLog.Contains("a échoué")));
    }

    [Fact]
    public async Task EmailFailureIsInvisibleForTheVisitorAndWrittenWithTheUserButWithoutTheAddress()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisLoginTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerDuringTheSimulatedEmailFailure = await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        Assert.Equal(HttpStatusCode.OK, answerDuringTheSimulatedEmailFailure.StatusCode);
        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedInMemoryForThisTest.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog.Any(messageWrittenInTheTechnicalLog =>
            messageWrittenInTheTechnicalLog.StartsWith("Error") && messageWrittenInTheTechnicalLog.Contains("n'a pas pu partir")));
        var textOfTheWholeTechnicalLog = string.Join("\n", apiStartedInMemoryForThisTest.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog);
        Assert.Contains(accountUsedByThisLoginTest.Id.ToString(), textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(EmailAddressOfTheLoginTests, textOfTheWholeTechnicalLog);
    }

    // Une panne ne doit pas arrêter la tâche de fond : la demande suivante part normalement.
    [Fact]
    public async Task FailedRequestDoesNotPreventTheNextRequestFromBeingProcessed()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.DatabaseIsDownDuringThisTest = true;
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);
        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedInMemoryForThisTest.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog.Any(messageWrittenInTheTechnicalLog =>
            messageWrittenInTheTechnicalLog.Contains("a échoué")));

        apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.DatabaseIsDownDuringThisTest = false;
        await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Count == 1);
    }

    // SS §11 : le journal note l'adresse IP de la demande.
    [Fact]
    public async Task IpAddressOfTheRequestIsWrittenInTheSecurityJournal()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientThatKeepsTheCookiesOfTheBrowser.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.7");

        await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "inconnu@exemple.test");

        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedInMemoryForThisTest.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents.Count == 1);
        var securityEventOfTheRecoveryRequest = apiStartedInMemoryForThisTest.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents[0];
        Assert.Equal(SecurityEventType.PasswordRecovery, securityEventOfTheRecoveryRequest.Type);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEventOfTheRecoveryRequest.IpAddress);
    }

    // --- Parcours complet

    [Fact]
    public async Task WholeRecoveryChangesThePasswordAndOnlyTheNewOneLogsIn()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        var answerToThePasswordResetRequest = await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink);

        Assert.Equal(HttpStatusCode.OK, answerToThePasswordResetRequest.StatusCode);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToThePasswordResetRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, ValidPasswordChosenInTheseTests)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, NewPasswordChosenInTheRecoveryTests)).StatusCode);
    }

    // GR Auth : la récupération ne connecte personne.
    [Fact]
    public async Task RecoveryDoesNotLogTheVisitorIn()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        var answerToThePasswordResetRequest = await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink);

        Assert.False(answerToThePasswordResetRequest.Headers.Contains("Set-Cookie"));
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientThatKeepsTheCookiesOfTheBrowser.GetAsync(AddressOfTheCsrfTokenEndpoint)).StatusCode);
    }

    // GR Auth et SS §3 : l'attaquant qui avait une session ouverte avec l'ancien mot de passe est déconnecté.
    [Fact]
    public async Task RecoveryClosesTheSessionsOpenedWithTheOldPassword()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientOfTheBrowserAlreadyLoggedIn = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientOfTheBrowserAlreadyLoggedIn);
        Assert.Equal(HttpStatusCode.OK, (await clientOfTheBrowserAlreadyLoggedIn.GetAsync(AddressOfTheCsrfTokenEndpoint)).StatusCode);
        var clientOfTheVisitorDoingTheRecovery = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientOfTheVisitorDoingTheRecovery, EmailAddressOfTheLoginTests);

        await SendResetPasswordRequestToTheApiAsync(clientOfTheVisitorDoingTheRecovery, tokenReadInTheRecoveryLink);

        Assert.Equal(HttpStatusCode.Unauthorized, (await clientOfTheBrowserAlreadyLoggedIn.GetAsync(AddressOfTheCsrfTokenEndpoint)).StatusCode);
    }

    // GR Auth : une récupération réussie débloque aussi le compte.
    [Fact]
    public async Task RecoveryAlsoUnlocksTheLockedAccount()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisLoginTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        accountUsedByThisLoginTest.FailedLoginAttemptCount = 5;
        accountUsedByThisLoginTest.LockoutCount = 2;
        accountUsedByThisLoginTest.LockoutEndsAt = apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddHours(1);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink);

        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, NewPasswordChosenInTheRecoveryTests)).StatusCode);
    }

    [Fact]
    public async Task SuccessfulResetIsWrittenInTheSecurityJournalWithTheIpAddress()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisLoginTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientThatKeepsTheCookiesOfTheBrowser.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.7");
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink);

        var securityEventOfThePasswordChange = Assert.Single(apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks);
        Assert.Equal(SecurityEventType.PasswordChange, securityEventOfThePasswordChange.Type);
        Assert.Equal(accountUsedByThisLoginTest.Id, securityEventOfThePasswordChange.UserId);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEventOfThePasswordChange.IpAddress);
    }

    // --- Liens et mots de passe refusés

    [Fact]
    public async Task UnknownLinkIsRefusedOnItsFieldWithTheExactMessage()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheUnknownLinkOfThisTest = await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "jeton-invente");

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheUnknownLinkOfThisTest.StatusCode);
        Assert.Equal("Ce lien n'est plus valable. Demande un nouveau lien avec « Mot de passe oublié ».", await ReadErrorMessageOfThisRequestFieldAsync(answerToTheUnknownLinkOfThisTest, "Token"));
    }

    [Fact]
    public async Task PasswordBreakingTheRulesIsRefusedOnItsFieldAndKeepsTheLinkUsable()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        var answerToTheWeakPasswordOfThisTest = await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink, "trop-court");

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheWeakPasswordOfThisTest.StatusCode);
        Assert.Equal(AccountRegistrationService.ErrorMessageForPasswordBreakingTheRules, await ReadErrorMessageOfThisRequestFieldAsync(answerToTheWeakPasswordOfThisTest, "NewPassword"));
        Assert.Equal(HttpStatusCode.OK, (await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink)).StatusCode);
    }

    [Fact]
    public async Task LinkCanOnlyBeUsedOnce()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);
        await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink);

        var answerToTheSecondUseOfTheLink = await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink, "Encore-Un-Mot-De-Passe-3");

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheSecondUseOfTheLink.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests, NewPasswordChosenInTheRecoveryTests)).StatusCode);
    }

    [Fact]
    public async Task LinkExpiresExactlyAfterThirtyMinutes()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(30));
        var answerToTheLatePasswordReset = await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheLatePasswordReset.StatusCode);
    }

    [Fact]
    public async Task SecondRequestReplacesTheFirstLink()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var tokenOfTheFirstRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);
        var tokenOfTheSecondRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenOfTheFirstRecoveryLink)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenOfTheSecondRecoveryLink)).StatusCode);
    }

    // GR Auth : une adresse non confirmée reçoit une invitation, jamais un lien.
    [Fact]
    public async Task UnconfirmedAddressReceivesAnInvitationWithoutAnyLink()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, emailAddressOfTheAccountIsConfirmed: false);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Count == 1);
        Assert.DoesNotContain("#token=", apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails[0].Body);
    }

    // Une adresse sans compte ne reçoit rien : on attend la trace du journal pour être sûr que la demande a été traitée.
    [Fact]
    public async Task UnknownAddressReceivesNoEmailAtAll()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "inconnu@exemple.test");

        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedInMemoryForThisTest.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents.Count == 1);
        Assert.Empty(apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails);
    }

    // --- Limites

    [Fact]
    public async Task AddressIsLimitedToThreeRequestsPerHourAndAnotherAddressIsNot()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        for (var numberOfTheRequestBeingSent = 1; numberOfTheRequestBeingSent <= 3; numberOfTheRequestBeingSent++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "inconnu@exemple.test")).StatusCode);
        }

        var answerOverTheLimitOfTheAddress = await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, " Inconnu@Exemple.TEST ");

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerOverTheLimitOfTheAddress, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");
        Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "autre@exemple.test")).StatusCode);
    }

    // Spammer l'inscription avec l'adresse d'une victime ne doit pas l'empêcher de retrouver son mot de passe.
    [Fact]
    public async Task RegistrationRequestsDoNotUseTheLimitOfTheRecovery()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        for (var numberOfTheRequestBeingSent = 1; numberOfTheRequestBeingSent <= 3; numberOfTheRequestBeingSent++)
        {
            var answerToTheRegistrationRequestOfThisTest = await clientThatKeepsTheCookiesOfTheBrowser.PostAsync(
                AddressOfTheAccountRegistrationEndpoint,
                new StringContent("{\"email\":\"victime@exemple.test\",\"password\":\"" + ValidPasswordChosenInTheseTests + "\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, answerToTheRegistrationRequestOfThisTest.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "victime@exemple.test")).StatusCode);
    }

    [Fact]
    public async Task ForgotPasswordIsLimitedToTenRequestsPerHourAndPerIpAddress()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientOfTheFirstIpAddress = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientOfTheFirstIpAddress.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.10");
        var clientOfTheSecondIpAddress = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientOfTheSecondIpAddress.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.11");

        for (var numberOfTheRequestBeingSent = 1; numberOfTheRequestBeingSent <= 10; numberOfTheRequestBeingSent++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestToTheApiAsync(clientOfTheFirstIpAddress, "visiteur-" + numberOfTheRequestBeingSent + "@exemple.test")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendForgotPasswordRequestToTheApiAsync(clientOfTheFirstIpAddress, "visiteur-11@exemple.test")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestToTheApiAsync(clientOfTheSecondIpAddress, "visiteur-12@exemple.test")).StatusCode);
    }

    [Fact]
    public async Task ResetIsLimitedToTenAttemptsPerHourAndPerIpAddress()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        for (var numberOfTheAttemptBeingSent = 1; numberOfTheAttemptBeingSent <= 10; numberOfTheAttemptBeingSent++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "jeton-invente")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "jeton-invente")).StatusCode);
    }

    // Les limites de chaque route sont séparées : épuiser l'une ne bloque pas l'autre.
    [Fact]
    public async Task LimitOfTheResetRouteDoesNotBlockTheForgotPasswordRoute()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        for (var numberOfTheAttemptBeingSent = 1; numberOfTheAttemptBeingSent <= 11; numberOfTheAttemptBeingSent++)
        {
            await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "jeton-invente");
        }

        Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "visiteur@exemple.test")).StatusCode);
    }

    // Le test ne peut pas attendre une heure : il vérifie la durée exacte de la limite (SS §6).
    [Fact]
    public void RecoveryLimitIsExactlyTenRequestsPerHour()
    {
        var optionsOfTheAccountRecoveryLimit = RateLimitingConfiguration.CreateOptionsOfTheAccountRecoveryRequestsLimit();

        Assert.Equal(10, optionsOfTheAccountRecoveryLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromHours(1), optionsOfTheAccountRecoveryLimit.Window);
    }

    // --- Entrées hostiles

    // Envoie un texte JSON écrit à la main sur cette route.
    private static async Task<HttpResponseMessage> SendRawJsonToThisEndpointAsync(HttpClient clientThatCallsTheApiInMemory, string addressOfTheEndpointBeingTested, string rawJsonSentByTheTest)
    {
        return await clientThatCallsTheApiInMemory.PostAsync(addressOfTheEndpointBeingTested, new StringContent(rawJsonSentByTheTest, Encoding.UTF8, "application/json"));
    }

    [Theory]
    [InlineData("""{"email":"sans-arobase"}""")]
    [InlineData("""{"email":"nul\u0000@exemple.test"}""")]
    [InlineData("""{"email":"' OR 1=1 --@exemple.test"}""")]
    public async Task MalformedAddressIsRefusedOnItsFieldAndNeverQueued(string rawJsonSentByTheTest)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestWithHostileValues = await SendRawJsonToThisEndpointAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheForgotPasswordEndpoint, rawJsonSentByTheTest);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestWithHostileValues.StatusCode);
        Assert.Equal(AccountRegistrationService.ErrorMessageForInvalidEmailAddressFormat, await ReadErrorMessageOfThisRequestFieldAsync(answerToTheRequestWithHostileValues, "Email"));
    }

    // Aucune de ces valeurs ne doit provoquer d'erreur 500 ni apparaître dans la réponse.
    [Theory]
    [InlineData("""{"token":"jeton","newPassword":"Abc\ud800"}""")]
    [InlineData("""{"token":"jeton\ud800","newPassword":"Nouveau-Mot-De-Passe-2"}""")]
    [InlineData("""{"token":"jeton","newPassword":"Abc￾"}""")]
    [InlineData("""{"token":"nul\u0000","newPassword":"Nouveau-Mot-De-Passe-2"}""")]
    [InlineData("""{"token":"' OR 1=1 --","newPassword":"Nouveau-Mot-De-Passe-2"}""")]
    [InlineData("""{"newPassword":"Nouveau-Mot-De-Passe-2"}""")]
    [InlineData("""{"token":"jeton"}""")]
    [InlineData("""{"token":null,"newPassword":null}""")]
    [InlineData("""{"token":12,"newPassword":34}""")]
    [InlineData("[]")]
    [InlineData("pas du JSON")]
    [InlineData("")]
    public async Task HostileResetRequestIsRefusedWithoutAnyErrorOrInternalName(string rawJsonSentByTheTest)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestWithHostileValues = await SendRawJsonToThisEndpointAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheResetPasswordEndpoint, rawJsonSentByTheTest);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestWithHostileValues.StatusCode);
        var textOfTheApiAnswerToCheck = await answerToTheRequestWithHostileValues.Content.ReadAsStringAsync();
        Assert.DoesNotContain("requestSentByTheVisitor", textOfTheApiAnswerToCheck);
        Assert.DoesNotContain("ResetPasswordRequest", textOfTheApiAnswerToCheck);
    }

    [Theory]
    [InlineData("""{"email":null}""", "L'adresse e-mail est obligatoire.")]
    [InlineData("""{}""", "L'adresse e-mail est obligatoire.")]
    public async Task MissingAddressIsRefusedWithItsFrenchMessage(string rawJsonSentByTheTest, string messageExpectedForTheRefusedField)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestOfThisTest = await SendRawJsonToThisEndpointAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheForgotPasswordEndpoint, rawJsonSentByTheTest);

        Assert.Equal(messageExpectedForTheRefusedField, await ReadErrorMessageOfThisRequestFieldAsync(answerToTheRequestOfThisTest, "Email"));
    }

    [Theory]
    [InlineData("""{"token":"jeton"}""", "NewPassword", "Le nouveau mot de passe est obligatoire.")]
    [InlineData("""{"newPassword":"Nouveau-Mot-De-Passe-2"}""", "Token", "Le lien de récupération est obligatoire.")]
    public async Task MissingResetFieldIsRefusedWithItsFrenchMessage(string rawJsonSentByTheTest, string nameOfTheFieldInError, string messageExpectedForTheRefusedField)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestOfThisTest = await SendRawJsonToThisEndpointAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheResetPasswordEndpoint, rawJsonSentByTheTest);

        Assert.Equal(messageExpectedForTheRefusedField, await ReadErrorMessageOfThisRequestFieldAsync(answerToTheRequestOfThisTest, nameOfTheFieldInError));
    }

    // Un site piégé ne peut envoyer qu'un formulaire ou du texte sans demander d'autorisation : ils sont refusés (SS §6).
    [Theory]
    [InlineData(AddressOfTheForgotPasswordEndpoint, "text/plain")]
    [InlineData(AddressOfTheForgotPasswordEndpoint, "application/x-www-form-urlencoded")]
    [InlineData(AddressOfTheResetPasswordEndpoint, "text/plain")]
    [InlineData(AddressOfTheResetPasswordEndpoint, "application/x-www-form-urlencoded")]
    [InlineData(AddressOfTheAccountUnlockEndpoint, "text/plain")]
    public async Task ContentTypesThatATrappedWebsiteCanSendAreRefused(string addressOfTheEndpointBeingTested, string contentTypeSentByTheTrappedWebsite)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var bodySentByTheTrappedWebsite = new StringContent("""{"email":"victime@exemple.test"}""", Encoding.UTF8, contentTypeSentByTheTrappedWebsite);

        var answerGivenToTheTrappedWebsite = await clientThatKeepsTheCookiesOfTheBrowser.PostAsync(addressOfTheEndpointBeingTested, bodySentByTheTrappedWebsite);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, answerGivenToTheTrappedWebsite.StatusCode);
    }

    // --- Pannes et journal technique

    [Fact]
    public async Task DatabaseFailureDuringResetIsAnsweredWithoutAnyTechnicalDetail()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        apiStartedLikeInProductionForThisTest.RepositoryThatKeepsAccountsInMemory.DatabaseIsDownWhenReadingTheLinks = true;
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedLikeInProductionForThisTest);

        var answerDuringTheSimulatedDatabaseFailure = await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, "jeton");

        Assert.Equal(HttpStatusCode.InternalServerError, answerDuringTheSimulatedDatabaseFailure.StatusCode);
        Assert.DoesNotContain(FakeUserAccountRepository.TechnicalDetailOfTheSimulatedDatabaseFailure, await answerDuringTheSimulatedDatabaseFailure.Content.ReadAsStringAsync());
    }

    // Au niveau le plus détaillé, le journal technique ne contient jamais de secret (SS §7).
    [Fact]
    public async Task TechnicalLogNeverContainsTheSecretsOfTheRecovery()
    {
        using var apiWithTheMostDetailedLog = new ApiFactoryLoggingEverything();
        AddAccountToTheFakeRepositoryOfTheApi(apiWithTheMostDetailedLog);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiWithTheMostDetailedLog);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(apiWithTheMostDetailedLog, clientThatKeepsTheCookiesOfTheBrowser, EmailAddressOfTheLoginTests);

        await SendResetPasswordRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, tokenReadInTheRecoveryLink);

        var textOfTheWholeTechnicalLog = string.Join("\n", apiWithTheMostDetailedLog.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog);
        Assert.NotEmpty(textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(EmailAddressOfTheLoginTests, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(tokenReadInTheRecoveryLink, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(NewPasswordChosenInTheRecoveryTests, textOfTheWholeTechnicalLog);
    }
}
