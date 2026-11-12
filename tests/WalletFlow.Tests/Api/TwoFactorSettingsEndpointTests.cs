using WalletFlow.Api.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Controllers;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes de réglage de la double authentification, de bout en bout (GR Auth, SS §2, §4 et §6).
public class TwoFactorSettingsEndpointTests
{
    // Calcule le premier code d'un secret à la date de la fausse horloge.
    private static object CreateCodeBody(ApiFactoryWithFakes api, string secret)
    {
        return new { code = CalculateCurrentCode(api, secret) };
    }

    // --- La session et le jeton anti-CSRF

    // SS §4 : aucune de ces routes ne s'ouvre sans session.
    [Theory]
    [InlineData(ActivationStartEndpoint)]
    [InlineData(ActivationFinishEndpoint)]
    [InlineData(DeactivationEndpoint)]
    public async Task RouteWithoutSessionIsAnsweredWithUnauthorizedStatus(string address)
    {
        using var api = new ApiFactoryWithFakes();
        var browserClient = CreateBrowserClient(api);

        var response = await SendSettingsRequestAsync(browserClient, address, new { password = ValidPassword, code = "123456" }, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // SS §6 : un site piégé ne peut pas déclencher ces routes avec le cookie du navigateur.
    [Theory]
    [InlineData(ActivationStartEndpoint)]
    [InlineData(ActivationFinishEndpoint)]
    [InlineData(DeactivationEndpoint)]
    public async Task RouteWithASessionButWithoutCsrfTokenIsRefused(string address)
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(browserClient);

        var response = await SendSettingsRequestAsync(browserClient, address, new { password = ValidPassword, code = "123456" }, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(account.IsTwoFactorEnabled);
    }

    // --- L'activation

    [Fact]
    public async Task RightPasswordGivesTheSecretAndItsLinkInAnAnswerNobodyKeeps()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);

        var response = await SendSettingsRequestAsync(browserClient, ActivationStartEndpoint, new { password = ValidPassword }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var secret = jsonDocument.RootElement.GetProperty("secret").GetString()!;
        Assert.Matches("^[A-Z2-7]{32}$", secret);
        Assert.Equal(
            "otpauth://totp/WalletFlow:utilisateur%40exemple.test?secret=" + secret + "&issuer=WalletFlow&algorithm=SHA1&digits=6&period=30",
            jsonDocument.RootElement.GetProperty("linkForTheAuthenticatorApplication").GetString());
        Assert.Contains("no-store", response.Headers.GetValues("Cache-Control").Single());
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
        Assert.False(account.IsTwoFactorEnabled);
        Assert.Null(account.EncryptedTotpSecret);
    }

    [Fact]
    public async Task WrongPasswordAnswersWithAFieldErrorAndNoSecret()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);

        var response = await SendSettingsRequestAsync(browserClient, ActivationStartEndpoint, new { password = WrongPassword }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.WrongCurrentPassword, await ReadFieldErrorAsync(response, "Password"));
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AccountAlreadyActivatedIsAnsweredWithConflictStatus()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInWithCodeThenGetCsrfTokenAsync(api, browserClient, activatedAccount.Secret);

        var response = await SendSettingsRequestAsync(browserClient, ActivationStartEndpoint, new { password = ValidPassword }, csrfToken);

        await AssertProblemTitleAsync(response, HttpStatusCode.Conflict, ErrorMessages.TwoFactorAlreadyEnabled);
    }

    [Fact]
    public async Task RightFirstCodeActivatesAndGivesTheBackupCodesInAnAnswerNobodyKeeps()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);
        var secret = await StartActivationThenReadSecretAsync(browserClient, csrfToken);

        var response = await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, CreateCodeBody(api, secret), csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(10, jsonDocument.RootElement.GetProperty("backupCodes").GetArrayLength());
        Assert.Contains("no-store", response.Headers.GetValues("Cache-Control").Single());
        Assert.True(account.IsTwoFactorEnabled);
        Assert.DoesNotContain(secret, account.EncryptedTotpSecret);

        // Le secret en base se relit avec la clé donnée à l'API : c'est bien cette clé qui l'a chiffré.
        var protector = new AesGcmTotpSecretProtector(api.TotpEncryptionKey);
        Assert.Equal(secret, protector.Decrypt(account.EncryptedTotpSecret!, account.Id));

        foreach (var backupCode in api.TwoFactorRepository.SavedBackupCodes)
        {
            Assert.StartsWith(LibsodiumBackupCodeHasher.HashPrefix, backupCode.CodeHash);
        }
    }

    [Fact]
    public async Task WrongFirstCodeAnswersWithAFieldErrorAndActivatesNothing()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);
        var secret = await StartActivationThenReadSecretAsync(browserClient, csrfToken);

        var response = await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, new { code = FindWrongCode(api, secret) }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.WrongTwoFactorCode, await ReadFieldErrorAsync(response, "Code"));
        Assert.False(account.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task FinishingWithoutAnyStartAnswers409()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);

        var response = await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, new { code = "123456" }, csrfToken);

        await AssertProblemTitleAsync(response, HttpStatusCode.Conflict, ErrorMessages.ActivationMustRestart);
    }

    // SS §4 : l'identifiant vient toujours de la session. Un champ ajouté à la requête ne change rien, et seuls les champs prévus sont lus.
    [Fact]
    public async Task FieldsAddedToTheRequestCannotActivateTheAccountOfSomeoneElse()
    {
        using var api = new ApiFactoryWithFakes();
        var attackerAccount = await AddAccountAsync(api);
        var victimAccount = await AddAccountAsync(api, "victime@exemple.test");
        var attackerClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(attackerClient);

        var response = await SendSettingsRequestAsync(attackerClient, ActivationStartEndpoint, new { password = ValidPassword, userId = victimAccount.Id, id = victimAccount.Id, email = victimAccount.Email }, csrfToken);
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var secret = jsonDocument.RootElement.GetProperty("secret").GetString()!;
        Assert.Contains("utilisateur%40exemple.test", jsonDocument.RootElement.GetProperty("linkForTheAuthenticatorApplication").GetString());

        await SendSettingsRequestAsync(attackerClient, ActivationFinishEndpoint, new { code = CalculateCurrentCode(api, secret), userId = victimAccount.Id }, csrfToken);

        Assert.True(attackerAccount.IsTwoFactorEnabled);
        Assert.False(victimAccount.IsTwoFactorEnabled);
        Assert.Null(victimAccount.EncryptedTotpSecret);
    }

    // GR Auth : seul un changement de mot de passe ferme les autres sessions.
    [Fact]
    public async Task OtherSessionsOfTheAccountStayOpenAtActivation()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var otherClient = CreateBrowserClient(api);
        await SendLoginRequestAsync(otherClient);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);
        var secret = await StartActivationThenReadSecretAsync(browserClient, csrfToken);

        await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, CreateCodeBody(api, secret), csrfToken);

        var otherResponse = await otherClient.GetAsync(CsrfTokenEndpoint);
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
    }

    // --- La désactivation

    [Fact]
    public async Task RightPasswordAndCodeDeactivateAndTheNextLoginNoLongerAsksForACode()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInWithCodeThenGetCsrfTokenAsync(api, browserClient, activatedAccount.Secret);
        WaitForNextCode(api);

        var deactivationResponse = await SendSettingsRequestAsync(browserClient, DeactivationEndpoint, new { password = ValidPassword, code = CalculateCurrentCode(api, activatedAccount.Secret) }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, deactivationResponse.StatusCode);
        var jsonDocument = JsonDocument.Parse(await deactivationResponse.Content.ReadAsStringAsync());
        Assert.Equal(TwoFactorSettingsController.DeactivatedMessage, jsonDocument.RootElement.GetProperty("message").GetString());
        Assert.False(activatedAccount.UserAccount.IsTwoFactorEnabled);
        Assert.Null(activatedAccount.UserAccount.EncryptedTotpSecret);
        Assert.Empty(api.TwoFactorRepository.SavedBackupCodes);

        var loginResponse = await SendLoginRequestAsync(browserClient);
        var loginDocument = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        Assert.False(loginDocument.RootElement.GetProperty("isTwoFactorCodeRequired").GetBoolean());
    }

    // La réponse ne dit pas lequel des deux champs est faux : le message n'est rattaché à aucun champ.
    [Fact]
    public async Task WrongPasswordOrCodeAnswersTheSameThingWithoutNamingTheField()
    {
        using var api = new ApiFactoryWithFakes();
        var activatedAccount = await AddActivatedAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInWithCodeThenGetCsrfTokenAsync(api, browserClient, activatedAccount.Secret);
        WaitForNextCode(api);

        var wrongPasswordResponse = await SendSettingsRequestAsync(browserClient, DeactivationEndpoint, new { password = WrongPassword, code = CalculateCurrentCode(api, activatedAccount.Secret) }, csrfToken);
        var wrongCodeResponse = await SendSettingsRequestAsync(browserClient, DeactivationEndpoint, new { password = ValidPassword, code = FindWrongCode(api, activatedAccount.Secret) }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, wrongPasswordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongCodeResponse.StatusCode);
        Assert.Equal(ErrorMessages.WrongPasswordOrCode, await ReadFieldErrorAsync(wrongPasswordResponse, ApiErrorConfiguration.GeneralErrorsKey));
        Assert.Equal(ErrorMessages.WrongPasswordOrCode, await ReadFieldErrorAsync(wrongCodeResponse, ApiErrorConfiguration.GeneralErrorsKey));
        Assert.True(activatedAccount.UserAccount.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task AccountWithoutTwoFactorAnswers409()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);

        var deactivationResponse = await SendSettingsRequestAsync(browserClient, DeactivationEndpoint, new { password = ValidPassword, code = "123456" }, csrfToken);

        await AssertProblemTitleAsync(deactivationResponse, HttpStatusCode.Conflict, ErrorMessages.TwoFactorNotEnabled);
    }

    [Theory]
    [InlineData(ActivationStartEndpoint, "Password", "Le mot de passe est obligatoire.")]
    [InlineData(ActivationFinishEndpoint, "Code", "Le code est obligatoire.")]
    [InlineData(DeactivationEndpoint, "Password", "Le mot de passe est obligatoire.")]
    [InlineData(DeactivationEndpoint, "Code", "Le code est obligatoire.")]
    public async Task MissingFieldAnswersWithItsStableFieldError(string address, string fieldName, string message)
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);

        var response = await SendSettingsRequestAsync(browserClient, address, new { }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await ReadFieldErrorAsync(response, fieldName));
    }

    // --- La limite de débit

    // SS §6 : 10 par heure et par adresse IP, avec un seul compteur pour les trois routes.
    [Fact]
    public async Task TheThreeRoutesShareOneLimitOfTenRequestsPerAddress()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);

        for (var requestNumber = 1; requestNumber <= 4; requestNumber++)
        {
            await SendSettingsRequestAsync(browserClient, ActivationStartEndpoint, new { password = WrongPassword }, csrfToken);
            await SendSettingsRequestAsync(browserClient, DeactivationEndpoint, new { password = WrongPassword, code = "123456" }, csrfToken);
        }

        var ninthResponse = await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, new { code = "123456" }, csrfToken);
        var tenthResponse = await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, new { code = "123456" }, csrfToken);
        Assert.Equal(HttpStatusCode.Conflict, ninthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, tenthResponse.StatusCode);

        var overLimitResponse = await SendSettingsRequestAsync(browserClient, ActivationStartEndpoint, new { password = ValidPassword }, csrfToken);
        await AssertProblemTitleAsync(overLimitResponse, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");

        browserClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.20");
        var otherAddressResponse = await SendSettingsRequestAsync(browserClient, ActivationStartEndpoint, new { password = ValidPassword }, csrfToken);
        Assert.Equal(HttpStatusCode.OK, otherAddressResponse.StatusCode);
    }

    [Fact]
    public void LimitOfTheSettingsRequestsIsWrittenWithItsExactValues()
    {
        var settingsLimit = RateLimitingConfiguration.CreateTwoFactorSettingsLimit();

        Assert.Equal(10, settingsLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromHours(1), settingsLimit.Window);
    }

    // --- Le parcours complet

    // Tout le cycle de vie, avec le vrai Argon2 et les vrais codes : activer, se reconnecter, utiliser un code de secours, changer de téléphone.
    [Fact]
    public async Task WholeLifeCycleOfTheTwoFactorWorksThroughTheApi()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);

        var firstCsrfToken = await LogInThenGetCsrfTokenAsync(browserClient);
        var firstPhoneSecret = await StartActivationThenReadSecretAsync(browserClient, firstCsrfToken);
        var activationResponse = await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, CreateCodeBody(api, firstPhoneSecret), firstCsrfToken);
        var givenCodes = JsonDocument.Parse(await activationResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("backupCodes");
        await SendLogoutRequestAsync(browserClient, firstCsrfToken);

        // Le téléphone est perdu : un code de secours permet d'entrer, puis d'en désactiver l'usage avec un autre.
        await SendLoginRequestAsync(browserClient);
        var backupCodeResponse = await SendTwoFactorLoginRequestAsync(browserClient, givenCodes[0].GetString()!);
        Assert.Equal(HttpStatusCode.OK, backupCodeResponse.StatusCode);

        var secondCsrfToken = await GetCsrfTokenAsync(browserClient);
        var deactivationResponse = await SendSettingsRequestAsync(browserClient, DeactivationEndpoint, new { password = ValidPassword, code = givenCodes[1].GetString() }, secondCsrfToken);
        Assert.Equal(HttpStatusCode.OK, deactivationResponse.StatusCode);

        // Le nouveau téléphone : un nouveau secret et 10 nouveaux codes.
        WaitForNextCode(api);
        var secondPhoneSecret = await StartActivationThenReadSecretAsync(browserClient, secondCsrfToken);
        var reactivationResponse = await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, CreateCodeBody(api, secondPhoneSecret), secondCsrfToken);
        Assert.Equal(HttpStatusCode.OK, reactivationResponse.StatusCode);
        Assert.NotEqual(firstPhoneSecret, secondPhoneSecret);
        Assert.True(account.IsTwoFactorEnabled);
        Assert.Equal(10, api.TwoFactorRepository.SavedBackupCodes.Count);

        Assert.Equal(1, CountJournalEvents(api, SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Success));
        Assert.Equal(2, CountJournalEvents(api, SecurityEventType.TwoFactorActivation, SecurityEventResult.Success));
    }

    // SS §11 : le journal note l'adresse IP de chaque requête de réglage, pour un refus comme pour une réussite.
    [Fact]
    public async Task JournalKeepsTheIpAddressOfEverySettingsRequest()
    {
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        browserClient.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.7");
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);
        var eventCount = api.SecurityEventRepository.SavedSecurityEvents.Count;

        await SendSettingsRequestAsync(browserClient, ActivationStartEndpoint, new { password = WrongPassword }, csrfToken);
        var secret = await StartActivationThenReadSecretAsync(browserClient, csrfToken);
        await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, new { code = FindWrongCode(api, secret) }, csrfToken);
        await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, CreateCodeBody(api, secret), csrfToken);
        await SendSettingsRequestAsync(browserClient, DeactivationEndpoint, new { password = WrongPassword, code = "123456" }, csrfToken);

        var securityEvents = api.SecurityEventRepository.SavedSecurityEvents.Skip(eventCount).ToList();
        Assert.Equal(4, securityEvents.Count);
        foreach (var securityEvent in securityEvents)
        {
            Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEvent.IpAddress);
        }
    }

    // Compte les événements de ce type et de ce résultat dans le faux journal de l'API.
    private static int CountJournalEvents(ApiFactoryWithFakes api, SecurityEventType type, SecurityEventResult result)
    {
        var eventCount = 0;

        foreach (var securityEvent in api.SecurityEventRepository.SavedSecurityEvents)
        {
            if (securityEvent.Type == type && securityEvent.Result == result)
            {
                eventCount = eventCount + 1;
            }
        }

        return eventCount;
    }

    // --- Le journal technique

    // Au niveau le plus détaillé, le journal technique ne contient jamais le secret, le code ni un code de secours (SS §7).
    [Fact]
    public async Task TechnicalLogNeverContainsTheSecretOrTheCodesOfTheActivation()
    {
        using var api = new ApiFactoryLoggingEverything();
        await AddAccountAsync(api);
        var browserClient = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);
        var secret = await StartActivationThenReadSecretAsync(browserClient, csrfToken);
        var firstCode = CalculateCurrentCode(api, secret);

        var activationResponse = await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, new { code = firstCode }, csrfToken);
        var givenCodes = JsonDocument.Parse(await activationResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("backupCodes");

        var logText = string.Join("\n", api.LoggerProvider.Messages);
        Assert.NotEmpty(logText);
        Assert.DoesNotContain(secret, logText);
        Assert.DoesNotContain(firstCode, logText);
        Assert.DoesNotContain(ValidPassword, logText);
        foreach (var backupCode in givenCodes.EnumerateArray())
        {
            Assert.DoesNotContain(backupCode.GetString()!, logText);
        }
    }

    // Une panne au moment d'activer répond 500, sans détail, et n'active rien (SS §10).
    [Fact]
    public async Task DatabaseFailureDuringTheActivationAnswersAGenericErrorInProduction()
    {
        using var productionApi = new ApiFactoryInProduction();
        var account = await AddAccountAsync(productionApi);
        var browserClient = CreateBrowserClient(productionApi);
        var csrfToken = await LogInThenGetCsrfTokenAsync(browserClient);
        var secret = await StartActivationThenReadSecretAsync(browserClient, csrfToken);
        productionApi.TwoFactorRepository.FailsWhenSavingSettings = true;

        var response = await SendSettingsRequestAsync(browserClient, ActivationFinishEndpoint, CreateCodeBody(productionApi, secret), csrfToken);

        await AssertProblemTitleAsync(response, HttpStatusCode.InternalServerError, "Une erreur est survenue. Réessaie plus tard.");
        Assert.DoesNotContain(FakeTwoFactorAuthenticationRepository.FailureDetail, await response.Content.ReadAsStringAsync());
        Assert.False(account.IsTwoFactorEnabled);
    }
}
