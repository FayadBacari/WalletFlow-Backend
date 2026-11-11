using WalletFlow.Api.Contracts;
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
    private const string RecoveryRequestedMessage = "Si un compte existe avec cette adresse, un e-mail vient d'être envoyé.";

    // --- Demande

    [Fact]
    public async Task ForgotPasswordAnswersTheExactMessageAndSendsTheLinkInTheBackground()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var response = await SendForgotPasswordRequestAsync(browserClient, Email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(RecoveryRequestedMessage, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);

        await WaitUntilAsync(() => api.EmailSender.SentEmails.Count == 1);
        var email = api.EmailSender.SentEmails[0];
        Assert.Equal(Email, email.RecipientAddress);
        Assert.Contains("/reset-password#token=", email.Body);
    }

    // Lit le code et le texte exact d'une réponse.
    private static async Task<string> DescribeResponseAsync(HttpResponseMessage response)
    {
        return (int)response.StatusCode + "|" + await response.Content.ReadAsStringAsync();
    }

    // GR Auth : la page affiche le même message, que l'adresse existe ou non.
    [Fact]
    public async Task AnswerIsExactlyTheSameForUnknownUnconfirmedAndConfirmedAddresses()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api, "compte-confirme@exemple.test");
        await AddAccountAsync(api, "compte-non-confirme@exemple.test", isEmailConfirmed: false);
        var browserClient = CreateBrowserClient(api);

        var confirmedResponse = await DescribeResponseAsync(await SendForgotPasswordRequestAsync(browserClient, "compte-confirme@exemple.test"));
        var unconfirmedResponse = await DescribeResponseAsync(await SendForgotPasswordRequestAsync(browserClient, "compte-non-confirme@exemple.test"));
        var unknownAddressResponse = await DescribeResponseAsync(await SendForgotPasswordRequestAsync(browserClient, "inconnu@exemple.test"));

        Assert.Equal(confirmedResponse, unconfirmedResponse);
        Assert.Equal(confirmedResponse, unknownAddressResponse);
    }

    // La route ne touche jamais à la base : même une panne de la base ne change pas sa réponse.
    [Fact]
    public async Task AnswerStaysTheSameEvenDuringADatabaseFailure()
    {
        using var productionApi = new ApiFactoryInProduction();
        productionApi.AccountRepository.IsDown = true;
        var browserClient = CreateBrowserClient(productionApi);

        var response = await SendForgotPasswordRequestAsync(browserClient, Email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await WaitUntilAsync(() => productionApi.LoggerProvider.Messages.Any(message =>
            message.StartsWith("Error") && message.Contains("a échoué")));
    }

    [Fact]
    public async Task EmailFailureIsInvisibleForTheVisitorAndWrittenWithTheUserButWithoutTheAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        api.EmailSender.AlwaysFails = true;
        var browserClient = CreateBrowserClient(api);

        var failureResponse = await SendForgotPasswordRequestAsync(browserClient, Email);

        Assert.Equal(HttpStatusCode.OK, failureResponse.StatusCode);
        await WaitUntilAsync(() => api.LoggerProvider.Messages.Any(message =>
            message.StartsWith("Error") && message.Contains("n'a pas pu partir")));
        var logText = string.Join("\n", api.LoggerProvider.Messages);
        Assert.Contains(account.Id.ToString(), logText);
        Assert.DoesNotContain(Email, logText);
    }

    // Une panne ne doit pas arrêter la tâche de fond : la demande suivante part normalement.
    [Fact]
    public async Task FailedRequestDoesNotPreventTheNextRequestFromBeingProcessed()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        api.AccountRepository.IsDown = true;
        var browserClient = CreateBrowserClient(api);

        await SendForgotPasswordRequestAsync(browserClient, Email);
        await WaitUntilAsync(() => api.LoggerProvider.Messages.Any(message =>
            message.Contains("a échoué")));

        api.AccountRepository.IsDown = false;
        await SendForgotPasswordRequestAsync(browserClient, Email);

        await WaitUntilAsync(() => api.EmailSender.SentEmails.Count == 1);
    }

    // SS §11 : le journal note l'adresse IP de la demande.
    [Fact]
    public async Task IpAddressOfTheRequestIsWrittenInTheSecurityJournal()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);
        browserClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.7");

        await SendForgotPasswordRequestAsync(browserClient, "inconnu@exemple.test");

        await WaitUntilAsync(() => api.SecurityEventRepository.SavedSecurityEvents.Count == 1);
        var securityEvent = api.SecurityEventRepository.SavedSecurityEvents[0];
        Assert.Equal(SecurityEventType.PasswordRecovery, securityEvent.Type);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEvent.IpAddress);
    }

    // --- Parcours complet

    [Fact]
    public async Task WholeRecoveryChangesThePasswordAndOnlyTheNewOneLogsIn()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);

        var response = await SendResetPasswordRequestAsync(browserClient, linkToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendLoginRequestAsync(browserClient, Email, ValidPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestAsync(browserClient, Email, NewPassword)).StatusCode);
    }

    // GR Auth : la récupération ne connecte personne.
    [Fact]
    public async Task RecoveryDoesNotLogTheVisitorIn()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);

        var response = await SendResetPasswordRequestAsync(browserClient, linkToken);

        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(api.SessionRepository.SavedSessions);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browserClient.GetAsync(CsrfTokenEndpoint)).StatusCode);
    }

    // GR Auth et SS §3 : l'attaquant qui avait une session ouverte avec l'ancien mot de passe est déconnecté.
    [Fact]
    public async Task RecoveryClosesTheSessionsOpenedWithTheOldPassword()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var loggedInClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(loggedInClient);
        Assert.Equal(HttpStatusCode.OK, (await loggedInClient.GetAsync(CsrfTokenEndpoint)).StatusCode);
        var recoveryClient = CreateBrowserClient(api);
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, recoveryClient, Email);

        await SendResetPasswordRequestAsync(recoveryClient, linkToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await loggedInClient.GetAsync(CsrfTokenEndpoint)).StatusCode);
    }

    // GR Auth : une récupération réussie débloque aussi le compte.
    [Fact]
    public async Task RecoveryAlsoUnlocksTheLockedAccount()
    {
        using var api = new ApiFactoryWithFakes();
        await AddLockedAccountAsync(api, Email, 2, api.Clock.GetUtcNow().UtcDateTime.AddHours(1));
        var browserClient = CreateBrowserClient(api);
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);

        await SendResetPasswordRequestAsync(browserClient, linkToken);

        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestAsync(browserClient, Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task SuccessfulResetIsWrittenInTheSecurityJournalWithTheIpAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        browserClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.7");
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);

        await SendResetPasswordRequestAsync(browserClient, linkToken);

        var securityEvent = Assert.Single(api.SecurityEventRepository.SavedSecurityEvents, securityEvent => securityEvent.Type == SecurityEventType.PasswordChange);
        Assert.Equal(SecurityEventType.PasswordChange, securityEvent.Type);
        Assert.Equal(account.Id, securityEvent.UserId);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEvent.IpAddress);
    }

    // --- Liens et mots de passe refusés

    [Fact]
    public async Task UnknownLinkIsRefusedOnItsFieldWithTheExactMessage()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await SendResetPasswordRequestAsync(browserClient, "jeton-invente");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Ce lien n'est plus valable. Demande un nouveau lien avec « Mot de passe oublié ».", await ReadFieldErrorAsync(response, "Token"));
    }

    [Fact]
    public async Task PasswordBreakingTheRulesIsRefusedOnItsFieldAndKeepsTheLinkUsable()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);

        var response = await SendResetPasswordRequestAsync(browserClient, linkToken, "trop-court");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.PasswordBreaksRules, await ReadFieldErrorAsync(response, "NewPassword"));
        Assert.Equal(HttpStatusCode.OK, (await SendResetPasswordRequestAsync(browserClient, linkToken)).StatusCode);
    }

    [Fact]
    public async Task LinkCanOnlyBeUsedOnce()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);
        await SendResetPasswordRequestAsync(browserClient, linkToken);

        var response = await SendResetPasswordRequestAsync(browserClient, linkToken, "Encore-Un-Mot-De-Passe-3");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestAsync(browserClient, Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task LinkExpiresExactlyAfterThirtyMinutes()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);

        api.Clock.Advance(TimeSpan.FromMinutes(30));
        var response = await SendResetPasswordRequestAsync(browserClient, linkToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SecondRequestReplacesTheFirstLink()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var firstRecoveryToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);
        var secondRecoveryToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendResetPasswordRequestAsync(browserClient, firstRecoveryToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendResetPasswordRequestAsync(browserClient, secondRecoveryToken)).StatusCode);
    }

    // GR Auth : une adresse non confirmée reçoit une invitation, jamais un lien.
    [Fact]
    public async Task UnconfirmedAddressReceivesAnInvitationWithoutAnyLink()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api, isEmailConfirmed: false);
        var browserClient = CreateBrowserClient(api);

        await SendForgotPasswordRequestAsync(browserClient, Email);

        await WaitUntilAsync(() => api.EmailSender.SentEmails.Count == 1);
        Assert.DoesNotContain("#token=", api.EmailSender.SentEmails[0].Body);
    }

    // Une adresse sans compte ne reçoit rien : on attend la trace du journal pour être sûr que la demande a été traitée.
    [Fact]
    public async Task UnknownAddressReceivesNoEmailAtAll()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        await SendForgotPasswordRequestAsync(browserClient, "inconnu@exemple.test");

        await WaitUntilAsync(() => api.SecurityEventRepository.SavedSecurityEvents.Count == 1);
        Assert.Empty(api.EmailSender.SentEmails);
    }

    // --- Limites

    [Fact]
    public async Task AddressIsLimitedToThreeRequestsPerHourAndAnotherAddressIsNot()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        for (var requestNumber = 1; requestNumber <= 3; requestNumber++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestAsync(browserClient, "inconnu@exemple.test")).StatusCode);
        }

        var response = await SendForgotPasswordRequestAsync(browserClient, " Inconnu@Exemple.TEST ");

        await AssertProblemTitleAsync(response, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");
        Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestAsync(browserClient, "autre@exemple.test")).StatusCode);
    }

    // Spammer l'inscription avec l'adresse d'une victime ne doit pas l'empêcher de retrouver son mot de passe.
    [Fact]
    public async Task RegistrationRequestsDoNotUseTheLimitOfTheRecovery()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        for (var requestNumber = 1; requestNumber <= 3; requestNumber++)
        {
            var response = await browserClient.PostAsync(
                RegistrationEndpoint,
                new StringContent("{\"email\":\"victime@exemple.test\",\"password\":\"" + ValidPassword + "\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestAsync(browserClient, "victime@exemple.test")).StatusCode);
    }

    [Fact]
    public async Task ForgotPasswordIsLimitedToTenRequestsPerHourAndPerIpAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var firstClient = CreateBrowserClient(api);
        firstClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.10");
        var secondClient = CreateBrowserClient(api);
        secondClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.11");

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestAsync(firstClient, "visiteur-" + requestNumber + "@exemple.test")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendForgotPasswordRequestAsync(firstClient, "visiteur-11@exemple.test")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestAsync(secondClient, "visiteur-12@exemple.test")).StatusCode);
    }

    [Fact]
    public async Task ResetIsLimitedToTenAttemptsPerHourAndPerIpAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        for (var attemptNumber = 1; attemptNumber <= 10; attemptNumber++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SendResetPasswordRequestAsync(browserClient, "jeton-invente")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendResetPasswordRequestAsync(browserClient, "jeton-invente")).StatusCode);
    }

    // Les limites de chaque route sont séparées : épuiser l'une ne bloque pas l'autre.
    [Fact]
    public async Task LimitOfTheResetRouteDoesNotBlockTheForgotPasswordRoute()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        for (var attemptNumber = 1; attemptNumber <= 11; attemptNumber++)
        {
            await SendResetPasswordRequestAsync(browserClient, "jeton-invente");
        }

        Assert.Equal(HttpStatusCode.OK, (await SendForgotPasswordRequestAsync(browserClient, "visiteur@exemple.test")).StatusCode);
    }

    // Le test ne peut pas attendre une heure : il vérifie la durée exacte de la limite (SS §6).
    [Fact]
    public void RecoveryLimitIsExactlyTenRequestsPerHour()
    {
        var recoveryLimit = RateLimitingConfiguration.CreateAccountRecoveryLimit();

        Assert.Equal(10, recoveryLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromHours(1), recoveryLimit.Window);
    }

    // --- Entrées hostiles

    // Envoie un texte JSON écrit à la main sur cette route.
    private static async Task<HttpResponseMessage> SendRawJsonAsync(HttpClient client, string address, string rawJson)
    {
        return await client.PostAsync(address, new StringContent(rawJson, Encoding.UTF8, "application/json"));
    }

    [Theory]
    [InlineData("""{"email":"sans-arobase"}""")]
    [InlineData("""{"email":"nul\u0000@exemple.test"}""")]
    [InlineData("""{"email":"' OR 1=1 --@exemple.test"}""")]
    public async Task MalformedAddressIsRefusedOnItsFieldAndNeverQueued(string rawJson)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await SendRawJsonAsync(browserClient, ForgotPasswordEndpoint, rawJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.InvalidEmailAddress, await ReadFieldErrorAsync(response, "Email"));
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
    public async Task HostileResetRequestIsRefusedWithoutAnyErrorOrInternalName(string rawJson)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await SendRawJsonAsync(browserClient, ResetPasswordEndpoint, rawJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("requestSentByTheVisitor", responseText);
        Assert.DoesNotContain("ResetPasswordRequest", responseText);
    }

    [Theory]
    [InlineData("""{"email":null}""", "L'adresse e-mail est obligatoire.")]
    [InlineData("""{}""", "L'adresse e-mail est obligatoire.")]
    public async Task MissingAddressIsRefusedWithItsFrenchMessage(string rawJson, string message)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await SendRawJsonAsync(browserClient, ForgotPasswordEndpoint, rawJson);

        Assert.Equal(message, await ReadFieldErrorAsync(response, "Email"));
    }

    [Theory]
    [InlineData("""{"token":"jeton"}""", "NewPassword", "Le nouveau mot de passe est obligatoire.")]
    [InlineData("""{"newPassword":"Nouveau-Mot-De-Passe-2"}""", "Token", "Le lien de récupération est obligatoire.")]
    public async Task MissingResetFieldIsRefusedWithItsFrenchMessage(string rawJson, string fieldName, string message)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await SendRawJsonAsync(browserClient, ResetPasswordEndpoint, rawJson);

        Assert.Equal(message, await ReadFieldErrorAsync(response, fieldName));
    }

    // Un site piégé ne peut envoyer qu'un formulaire ou du texte sans demander d'autorisation : ils sont refusés (SS §6).
    [Theory]
    [InlineData(ForgotPasswordEndpoint, "text/plain")]
    [InlineData(ForgotPasswordEndpoint, "application/x-www-form-urlencoded")]
    [InlineData(ResetPasswordEndpoint, "text/plain")]
    [InlineData(ResetPasswordEndpoint, "application/x-www-form-urlencoded")]
    [InlineData(UnlockEndpoint, "text/plain")]
    public async Task ContentTypesThatATrappedWebsiteCanSendAreRefused(string address, string contentType)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);
        var body = new StringContent("""{"email":"victime@exemple.test"}""", Encoding.UTF8, contentType);

        var response = await browserClient.PostAsync(address, body);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // --- Pannes et journal technique

    [Fact]
    public async Task DatabaseFailureDuringResetIsAnsweredWithoutAnyTechnicalDetail()
    {
        using var productionApi = new ApiFactoryInProduction();
        productionApi.AccountRepository.FailsWhenReadingLinks = true;
        var browserClient = CreateBrowserClient(productionApi);

        var response = await SendResetPasswordRequestAsync(browserClient, "jeton");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(FakeUserAccountRepository.FailureDetail, await response.Content.ReadAsStringAsync());
    }

    // Au niveau le plus détaillé, le journal technique ne contient jamais de secret (SS §7).
    [Fact]
    public async Task TechnicalLogNeverContainsTheSecretsOfTheRecovery()
    {
        using var api = new ApiFactoryLoggingEverything();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);

        await SendResetPasswordRequestAsync(browserClient, linkToken);

        var logText = string.Join("\n", api.LoggerProvider.Messages);
        Assert.NotEmpty(logText);
        Assert.DoesNotContain(Email, logText);
        Assert.DoesNotContain(linkToken, logText);
        Assert.DoesNotContain(NewPassword, logText);
    }
}
