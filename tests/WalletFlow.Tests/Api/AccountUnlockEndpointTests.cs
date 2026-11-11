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
    private static async Task<string> LockAccountThenReadUnlockTokenAsync(ApiFactoryWithFakes api, HttpClient client)
    {
        var emailCount = api.EmailSender.SentEmails.Count;

        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            await SendLoginRequestAsync(client, Email, WrongPassword);
        }

        await WaitUntilAsync(() => api.EmailSender.SentEmails.Count == emailCount + 1);

        return ReadConfirmationToken(api.EmailSender.SentEmails.Last().Body);
    }

    [Fact]
    public async Task FifthWrongPasswordSendsTheUnlockEmailInTheBackground()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        await LockAccountThenReadUnlockTokenAsync(api, browserClient);

        var email = Assert.Single(api.EmailSender.SentEmails);
        Assert.Equal(Email, email.RecipientAddress);
        Assert.Equal("Ton compte WalletFlow est bloqué", email.Subject);
        Assert.Contains("/unlock-account#token=", email.Body);
        Assert.Contains("pendant 15 minutes", email.Body);
    }

    // GR Auth : le lien débloque le compte, mais ne connecte pas l'utilisateur.
    [Fact]
    public async Task UnlockLinkOpensTheAccountWithoutLoggingIn()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await LockAccountThenReadUnlockTokenAsync(api, browserClient);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendLoginRequestAsync(browserClient)).StatusCode);

        var response = await SendUnlockRequestAsync(browserClient, linkToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AccountUnlockController.UnlockedMessage, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(api.SessionRepository.SavedSessions);
        Assert.Equal(1, account.LockoutCount);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);

        Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestAsync(browserClient)).StatusCode);
    }

    // SS §2 : un seul e-mail par blocage, pour qu'un attaquant ne puisse pas inonder la boîte de la victime.
    [Fact]
    public async Task AttemptsDuringTheLockoutSendNoOtherEmail()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await LockAccountThenReadUnlockTokenAsync(api, browserClient);

        await SendLoginRequestAsync(browserClient, Email, WrongPassword);
        await SendLoginRequestAsync(browserClient);

        // On envoie une dernière demande qui passe par la même file : quand son e-mail arrive, tout ce qui la précède a été traité.
        await AddAccountAsync(api, "temoin@exemple.test");
        await RequestRecoveryThenReadTokenAsync(api, browserClient, "temoin@exemple.test");

        Assert.Equal(1, api.EmailSender.SentEmails.Count(email => email.RecipientAddress == Email));
    }

    [Fact]
    public async Task UnlockLinkCanOnlyBeUsedOnce()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await LockAccountThenReadUnlockTokenAsync(api, browserClient);
        await SendUnlockRequestAsync(browserClient, linkToken);

        var response = await SendUnlockRequestAsync(browserClient, linkToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownLinkIsRefusedOnItsFieldWithTheExactMessage()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await SendUnlockRequestAsync(browserClient, "jeton-invente");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Ce lien n'est plus valable. Si ton compte est encore bloqué, utilise « Mot de passe oublié ».", await ReadFieldErrorAsync(response, "Token"));
    }

    // Un lien ne sert jamais à un autre usage : un lien de déblocage ne doit jamais changer un mot de passe.
    [Fact]
    public async Task UnlockLinkIsRefusedOnThePasswordResetRoute()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await LockAccountThenReadUnlockTokenAsync(api, browserClient);

        var response = await SendResetPasswordRequestAsync(browserClient, linkToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RecoveryLinkIsRefusedOnTheUnlockRoute()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await RequestRecoveryThenReadTokenAsync(api, browserClient, Email);

        var response = await SendUnlockRequestAsync(browserClient, linkToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Un nouveau blocage envoie un nouveau lien : l'ancien ne marche plus (SS §2).
    [Fact]
    public async Task NewLockoutReplacesTheOldUnlockLink()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var firstUnlockToken = await LockAccountThenReadUnlockTokenAsync(api, browserClient);

        api.Clock.Advance(TimeSpan.FromMinutes(15));
        var secondUnlockToken = await LockAccountThenReadUnlockTokenAsync(api, browserClient);

        Assert.Contains("pendant 30 minutes", api.EmailSender.SentEmails.Last().Body);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendUnlockRequestAsync(browserClient, firstUnlockToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendUnlockRequestAsync(browserClient, secondUnlockToken)).StatusCode);
    }

    // La panne de l'e-mail ne change pas la réponse de la connexion : elle révélerait que le compte existe (SS §2).
    [Fact]
    public async Task EmailFailureDoesNotChangeTheLoginAnswerAndIsWrittenInTheTechnicalLog()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        api.EmailSender.AlwaysFails = true;
        var browserClient = CreateBrowserClient(api);

        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            var response = await SendLoginRequestAsync(browserClient, Email, WrongPassword);
            await AssertProblemTitleAsync(response, HttpStatusCode.Unauthorized, "Identifiants incorrects");
        }

        await WaitUntilAsync(() => api.LoggerProvider.Messages.Any(message =>
            message.StartsWith("Error") && message.Contains("n'a pas pu partir")));
    }

    [Fact]
    public async Task UnlockIsLimitedToTenAttemptsPerHourAndPerIpAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        for (var attemptNumber = 1; attemptNumber <= 10; attemptNumber++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SendUnlockRequestAsync(browserClient, "jeton-invente")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendUnlockRequestAsync(browserClient, "jeton-invente")).StatusCode);
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
    public async Task HostileUnlockRequestIsRefusedWithoutAnyErrorOrInternalName(string rawJson)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await browserClient.PostAsync(
            UnlockEndpoint,
            new StringContent(rawJson, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("requestSentByTheVisitor", responseText);
        Assert.DoesNotContain("UnlockAccountRequest", responseText);
    }

    [Fact]
    public async Task MissingTokenIsRefusedWithItsFrenchMessage()
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await browserClient.PostAsync(UnlockEndpoint, new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal("Le lien de déblocage est obligatoire.", await ReadFieldErrorAsync(response, "Token"));
    }

    [Fact]
    public async Task SuccessfulUnlockIsWrittenInTheSecurityJournalWithTheIpAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        browserClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.7");
        var linkToken = await LockAccountThenReadUnlockTokenAsync(api, browserClient);

        await SendUnlockRequestAsync(browserClient, linkToken);

        var securityEvent = Assert.Single(api.SecurityEventRepository.SavedSecurityEvents, securityEvent => securityEvent.Type == SecurityEventType.Unlock);
        Assert.Equal(SecurityEventType.Unlock, securityEvent.Type);
        Assert.Equal(account.Id, securityEvent.UserId);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEvent.IpAddress);
    }

    // Au niveau le plus détaillé, le journal technique ne contient jamais de secret (SS §7).
    [Fact]
    public async Task TechnicalLogNeverContainsTheSecretsOfTheUnlock()
    {
        using var api = new ApiFactoryLoggingEverything();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var linkToken = await LockAccountThenReadUnlockTokenAsync(api, browserClient);

        await SendUnlockRequestAsync(browserClient, linkToken);

        var logText = string.Join("\n", api.LoggerProvider.Messages);
        Assert.NotEmpty(logText);
        Assert.DoesNotContain(Email, logText);
        Assert.DoesNotContain(linkToken, logText);
        Assert.DoesNotContain(WrongPassword, logText);
    }
}
