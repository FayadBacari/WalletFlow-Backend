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
    private static object CreateBodyWithTheCodeOfThisSecret(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest, string secretSharedWithTheAuthenticatorApplication)
    {
        return new { code = CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, secretSharedWithTheAuthenticatorApplication) };
    }

    // --- La session et le jeton anti-CSRF

    // SS §4 : aucune de ces routes ne s'ouvre sans session.
    [Theory]
    [InlineData(AddressOfTheActivationStartEndpoint)]
    [InlineData(AddressOfTheActivationFinishEndpoint)]
    [InlineData(AddressOfTheTwoFactorDeactivationEndpoint)]
    public async Task RouteWithoutSessionIsAnsweredWithUnauthorizedStatus(string addressOfTheSettingsEndpointBeingTested)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var answerToTheRequestOnTheSettingsRoute = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, addressOfTheSettingsEndpointBeingTested, new { password = ValidPasswordChosenInTheseTests, code = "123456" }, null);

        Assert.Equal(HttpStatusCode.Unauthorized, answerToTheRequestOnTheSettingsRoute.StatusCode);
    }

    // SS §6 : un site piégé ne peut pas déclencher ces routes avec le cookie du navigateur.
    [Theory]
    [InlineData(AddressOfTheActivationStartEndpoint)]
    [InlineData(AddressOfTheActivationFinishEndpoint)]
    [InlineData(AddressOfTheTwoFactorDeactivationEndpoint)]
    public async Task RouteWithASessionButWithoutCsrfTokenIsRefused(string addressOfTheSettingsEndpointBeingTested)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisTwoFactorTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheRequestOnTheSettingsRoute = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, addressOfTheSettingsEndpointBeingTested, new { password = ValidPasswordChosenInTheseTests, code = "123456" }, null);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestOnTheSettingsRoute.StatusCode);
        Assert.False(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
    }

    // --- L'activation

    [Fact]
    public async Task RightPasswordGivesTheSecretAndItsLinkInAnAnswerNobodyKeeps()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisTwoFactorTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheStartOfTheActivation = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationStartEndpoint, new { password = ValidPasswordChosenInTheseTests }, csrfTokenOfTheLoggedInUser);

        Assert.Equal(HttpStatusCode.OK, answerToTheStartOfTheActivation.StatusCode);
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheStartOfTheActivation.Content.ReadAsStringAsync());
        var secretOfThePhoneGivenByTheApi = jsonDocumentOfTheApiAnswer.RootElement.GetProperty("secret").GetString()!;
        Assert.Matches("^[A-Z2-7]{32}$", secretOfThePhoneGivenByTheApi);
        Assert.Equal(
            "otpauth://totp/WalletFlow:utilisateur%40exemple.test?secret=" + secretOfThePhoneGivenByTheApi + "&issuer=WalletFlow&algorithm=SHA1&digits=6&period=30",
            jsonDocumentOfTheApiAnswer.RootElement.GetProperty("linkForTheAuthenticatorApplication").GetString());
        Assert.Contains("no-store", answerToTheStartOfTheActivation.Headers.GetValues("Cache-Control").Single());
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheStartOfTheActivation);
        Assert.False(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
        Assert.Null(accountUsedByThisTwoFactorTest.EncryptedTotpSecret);
    }

    [Fact]
    public async Task WrongPasswordAnswersWithAFieldErrorAndNoSecret()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheStartOfTheActivation = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationStartEndpoint, new { password = WrongPasswordTypedInTheLoginTests }, csrfTokenOfTheLoggedInUser);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheStartOfTheActivation.StatusCode);
        Assert.Equal(TwoFactorActivationService.ErrorMessageForWrongPasswordAtActivation, await ReadErrorMessageOfThisRequestFieldAsync(answerToTheStartOfTheActivation, "Password"));
        Assert.DoesNotContain("secret", await answerToTheStartOfTheActivation.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AccountAlreadyActivatedIsAnsweredWithConflictStatus()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordAndCodeThenAskForTheCsrfTokenAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);

        var answerToTheStartOfTheActivation = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationStartEndpoint, new { password = ValidPasswordChosenInTheseTests }, csrfTokenOfTheLoggedInUser);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheStartOfTheActivation, HttpStatusCode.Conflict, TwoFactorActivationService.ErrorMessageForTwoFactorAlreadyEnabled);
    }

    [Fact]
    public async Task RightFirstCodeActivatesAndGivesTheBackupCodesInAnAnswerNobodyKeeps()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisTwoFactorTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var secretOfThePhoneGivenByTheApi = await StartActivationThenReadTheSecretAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);

        var answerToTheEndOfTheActivation = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, CreateBodyWithTheCodeOfThisSecret(apiStartedInMemoryForThisTest, secretOfThePhoneGivenByTheApi), csrfTokenOfTheLoggedInUser);

        Assert.Equal(HttpStatusCode.OK, answerToTheEndOfTheActivation.StatusCode);
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheEndOfTheActivation.Content.ReadAsStringAsync());
        Assert.Equal(10, jsonDocumentOfTheApiAnswer.RootElement.GetProperty("backupCodes").GetArrayLength());
        Assert.Contains("no-store", answerToTheEndOfTheActivation.Headers.GetValues("Cache-Control").Single());
        Assert.True(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
        Assert.DoesNotContain(secretOfThePhoneGivenByTheApi, accountUsedByThisTwoFactorTest.EncryptedTotpSecret);

        // Le secret en base se relit avec la clé donnée à l'API : c'est bien cette clé qui l'a chiffré.
        var protectorWithTheKeyOfTheApi = new AesGcmTotpSecretProtector(apiStartedInMemoryForThisTest.EncryptionKeyOfTheTotpSecretsInTheseTests);
        Assert.Equal(secretOfThePhoneGivenByTheApi, protectorWithTheKeyOfTheApi.DecryptSecretOfThisUserAccount(accountUsedByThisTwoFactorTest.EncryptedTotpSecret!, accountUsedByThisTwoFactorTest.Id));

        foreach (var backupCodeSavedInTheFakeRepository in apiStartedInMemoryForThisTest.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes)
        {
            Assert.StartsWith("$argon2id$", backupCodeSavedInTheFakeRepository.CodeHash);
        }
    }

    [Fact]
    public async Task WrongFirstCodeAnswersWithAFieldErrorAndActivatesNothing()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisTwoFactorTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var secretOfThePhoneGivenByTheApi = await StartActivationThenReadTheSecretAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);

        var answerToTheEndOfTheActivation = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, new { code = FindCodeThatIsWrongNow(apiStartedInMemoryForThisTest, secretOfThePhoneGivenByTheApi) }, csrfTokenOfTheLoggedInUser);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheEndOfTheActivation.StatusCode);
        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, await ReadErrorMessageOfThisRequestFieldAsync(answerToTheEndOfTheActivation, "Code"));
        Assert.False(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task FinishingWithoutAnyStartAnswers409()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheEndOfTheActivation = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, new { code = "123456" }, csrfTokenOfTheLoggedInUser);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheEndOfTheActivation, HttpStatusCode.Conflict, TwoFactorActivationService.ErrorMessageForActivationThatMustRestart);
    }

    // SS §4 : l'identifiant vient toujours de la session. Un champ ajouté à la requête ne change rien, et seuls les champs prévus sont lus.
    [Fact]
    public async Task FieldsAddedToTheRequestCannotActivateTheAccountOfSomeoneElse()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountOwnedByTheAttackerOfThisTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var accountOwnedByTheVictimOfThisTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, "victime@exemple.test");
        var clientUsedByTheAttackerOfThisTest = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenGivenToTheAttackerSession = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientUsedByTheAttackerOfThisTest);

        var answerToTheStartOfTheActivation = await SendSettingsRequestWithThisCsrfTokenAsync(clientUsedByTheAttackerOfThisTest, AddressOfTheActivationStartEndpoint, new { password = ValidPasswordChosenInTheseTests, userId = accountOwnedByTheVictimOfThisTest.Id, id = accountOwnedByTheVictimOfThisTest.Id, email = accountOwnedByTheVictimOfThisTest.Email }, csrfTokenGivenToTheAttackerSession);
        var jsonDocumentOfTheStartAnswer = JsonDocument.Parse(await answerToTheStartOfTheActivation.Content.ReadAsStringAsync());
        var secretOfThePhoneGivenByTheApi = jsonDocumentOfTheStartAnswer.RootElement.GetProperty("secret").GetString()!;
        Assert.Contains("utilisateur%40exemple.test", jsonDocumentOfTheStartAnswer.RootElement.GetProperty("linkForTheAuthenticatorApplication").GetString());

        await SendSettingsRequestWithThisCsrfTokenAsync(clientUsedByTheAttackerOfThisTest, AddressOfTheActivationFinishEndpoint, new { code = CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, secretOfThePhoneGivenByTheApi), userId = accountOwnedByTheVictimOfThisTest.Id }, csrfTokenGivenToTheAttackerSession);

        Assert.True(accountOwnedByTheAttackerOfThisTest.IsTwoFactorEnabled);
        Assert.False(accountOwnedByTheVictimOfThisTest.IsTwoFactorEnabled);
        Assert.Null(accountOwnedByTheVictimOfThisTest.EncryptedTotpSecret);
    }

    // GR Auth : seul un changement de mot de passe ferme les autres sessions.
    [Fact]
    public async Task OtherSessionsOfTheAccountStayOpenAtActivation()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientOfTheOtherBrowserOfTheUser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        await SendLoginRequestToTheApiAsync(clientOfTheOtherBrowserOfTheUser);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var secretOfThePhoneGivenByTheApi = await StartActivationThenReadTheSecretAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);

        await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, CreateBodyWithTheCodeOfThisSecret(apiStartedInMemoryForThisTest, secretOfThePhoneGivenByTheApi), csrfTokenOfTheLoggedInUser);

        var answerGivenToTheOtherBrowserOfTheUser = await clientOfTheOtherBrowserOfTheUser.GetAsync(AddressOfTheCsrfTokenEndpoint);
        Assert.Equal(HttpStatusCode.OK, answerGivenToTheOtherBrowserOfTheUser.StatusCode);
    }

    // --- La désactivation

    [Fact]
    public async Task RightPasswordAndCodeDeactivateAndTheNextLoginNoLongerAsksForACode()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordAndCodeThenAskForTheCsrfTokenAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);
        WaitForTheNextCodeOnThePhone(apiStartedInMemoryForThisTest);

        var answerToTheDeactivationOfTheTwoFactor = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheTwoFactorDeactivationEndpoint, new { password = ValidPasswordChosenInTheseTests, code = CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication) }, csrfTokenOfTheLoggedInUser);

        Assert.Equal(HttpStatusCode.OK, answerToTheDeactivationOfTheTwoFactor.StatusCode);
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheDeactivationOfTheTwoFactor.Content.ReadAsStringAsync());
        Assert.Equal(TwoFactorSettingsController.MessageSentWhenTheTwoFactorIsDeactivated, jsonDocumentOfTheApiAnswer.RootElement.GetProperty("message").GetString());
        Assert.False(activatedAccountUsedByThisTest.UserAccount.IsTwoFactorEnabled);
        Assert.Null(activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret);
        Assert.Empty(apiStartedInMemoryForThisTest.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes);

        var answerToTheNextLoginOfTheAccount = await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var jsonDocumentOfTheNextLogin = JsonDocument.Parse(await answerToTheNextLoginOfTheAccount.Content.ReadAsStringAsync());
        Assert.False(jsonDocumentOfTheNextLogin.RootElement.GetProperty("isTwoFactorCodeRequired").GetBoolean());
    }

    // La réponse ne dit pas lequel des deux champs est faux : le message n'est rattaché à aucun champ.
    [Fact]
    public async Task WrongPasswordOrCodeAnswersTheSameThingWithoutNamingTheField()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var activatedAccountUsedByThisTest = AddActivatedAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordAndCodeThenAskForTheCsrfTokenAsync(apiStartedInMemoryForThisTest, clientThatKeepsTheCookiesOfTheBrowser, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);
        WaitForTheNextCodeOnThePhone(apiStartedInMemoryForThisTest);

        var answerGivenWithTheWrongAccountPassword = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheTwoFactorDeactivationEndpoint, new { password = WrongPasswordTypedInTheLoginTests, code = CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication) }, csrfTokenOfTheLoggedInUser);
        var answerGivenWithTheWrongTwoFactorCode = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheTwoFactorDeactivationEndpoint, new { password = ValidPasswordChosenInTheseTests, code = FindCodeThatIsWrongNow(apiStartedInMemoryForThisTest, activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication) }, csrfTokenOfTheLoggedInUser);

        Assert.Equal(HttpStatusCode.BadRequest, answerGivenWithTheWrongAccountPassword.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, answerGivenWithTheWrongTwoFactorCode.StatusCode);
        Assert.Equal(TwoFactorDeactivationService.ErrorMessageForWrongPasswordOrCode, await ReadErrorMessageOfThisRequestFieldAsync(answerGivenWithTheWrongAccountPassword, ApiErrorConfiguration.KeyOfTheErrorsOutsideTheRequestFields));
        Assert.Equal(TwoFactorDeactivationService.ErrorMessageForWrongPasswordOrCode, await ReadErrorMessageOfThisRequestFieldAsync(answerGivenWithTheWrongTwoFactorCode, ApiErrorConfiguration.KeyOfTheErrorsOutsideTheRequestFields));
        Assert.True(activatedAccountUsedByThisTest.UserAccount.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task AccountWithoutTwoFactorAnswers409()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheDeactivationOfTheTwoFactor = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheTwoFactorDeactivationEndpoint, new { password = ValidPasswordChosenInTheseTests, code = "123456" }, csrfTokenOfTheLoggedInUser);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheDeactivationOfTheTwoFactor, HttpStatusCode.Conflict, TwoFactorDeactivationService.ErrorMessageForTwoFactorNotEnabled);
    }

    [Theory]
    [InlineData(AddressOfTheActivationStartEndpoint, "Password", "Le mot de passe est obligatoire.")]
    [InlineData(AddressOfTheActivationFinishEndpoint, "Code", "Le code est obligatoire.")]
    [InlineData(AddressOfTheTwoFactorDeactivationEndpoint, "Password", "Le mot de passe est obligatoire.")]
    [InlineData(AddressOfTheTwoFactorDeactivationEndpoint, "Code", "Le code est obligatoire.")]
    public async Task MissingFieldAnswersWithItsStableFieldError(string addressOfTheSettingsEndpointBeingTested, string nameOfTheFieldLeftOut, string messageExpectedForTheFieldLeftOut)
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);

        var answerToTheRequestOnTheSettingsRoute = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, addressOfTheSettingsEndpointBeingTested, new { }, csrfTokenOfTheLoggedInUser);

        Assert.Equal(HttpStatusCode.BadRequest, answerToTheRequestOnTheSettingsRoute.StatusCode);
        Assert.Equal(messageExpectedForTheFieldLeftOut, await ReadErrorMessageOfThisRequestFieldAsync(answerToTheRequestOnTheSettingsRoute, nameOfTheFieldLeftOut));
    }

    // --- La limite de débit

    // SS §6 : 10 par heure et par adresse IP, avec un seul compteur pour les trois routes.
    [Fact]
    public async Task TheThreeRoutesShareOneLimitOfTenRequestsPerAddress()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);

        for (var numberOfTheRequestBeingSent = 1; numberOfTheRequestBeingSent <= 4; numberOfTheRequestBeingSent++)
        {
            await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationStartEndpoint, new { password = WrongPasswordTypedInTheLoginTests }, csrfTokenOfTheLoggedInUser);
            await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheTwoFactorDeactivationEndpoint, new { password = WrongPasswordTypedInTheLoginTests, code = "123456" }, csrfTokenOfTheLoggedInUser);
        }

        var answerToTheNinthRequestOfTheLimit = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, new { code = "123456" }, csrfTokenOfTheLoggedInUser);
        var answerToTheTenthRequestOfTheLimit = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, new { code = "123456" }, csrfTokenOfTheLoggedInUser);
        Assert.Equal(HttpStatusCode.Conflict, answerToTheNinthRequestOfTheLimit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, answerToTheTenthRequestOfTheLimit.StatusCode);

        var answerToTheRequestOverTheLimit = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationStartEndpoint, new { password = ValidPasswordChosenInTheseTests }, csrfTokenOfTheLoggedInUser);
        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerToTheRequestOverTheLimit, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");

        clientThatKeepsTheCookiesOfTheBrowser.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.20");
        var answerGivenToRequestFromAnotherAddress = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationStartEndpoint, new { password = ValidPasswordChosenInTheseTests }, csrfTokenOfTheLoggedInUser);
        Assert.Equal(HttpStatusCode.OK, answerGivenToRequestFromAnotherAddress.StatusCode);
    }

    [Fact]
    public void LimitOfTheSettingsRequestsIsWrittenWithItsExactValues()
    {
        var optionsOfTheSettingsRequestsLimit = RateLimitingConfiguration.CreateOptionsOfTheTwoFactorSettingsRequestsLimit();

        Assert.Equal(10, optionsOfTheSettingsRequestsLimit.PermitLimit);
        Assert.Equal(TimeSpan.FromHours(1), optionsOfTheSettingsRequestsLimit.Window);
    }

    // --- Le parcours complet

    // Tout le cycle de vie, avec le vrai Argon2 et les vrais codes : activer, se reconnecter, utiliser un code de secours, changer de téléphone.
    [Fact]
    public async Task WholeLifeCycleOfTheTwoFactorWorksThroughTheApi()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        var accountUsedByThisTwoFactorTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);

        var csrfTokenOfTheFirstSession = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var secretOfTheFirstPhoneOfTheUser = await StartActivationThenReadTheSecretAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheFirstSession);
        var answerToTheActivationOfTheTwoFactor = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, CreateBodyWithTheCodeOfThisSecret(apiStartedInMemoryForThisTest, secretOfTheFirstPhoneOfTheUser), csrfTokenOfTheFirstSession);
        var backupCodesGivenToTheUserAtActivation = JsonDocument.Parse(await answerToTheActivationOfTheTwoFactor.Content.ReadAsStringAsync()).RootElement.GetProperty("backupCodes");
        await SendLogoutRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheFirstSession);

        // Le téléphone est perdu : un code de secours permet d'entrer, puis d'en désactiver l'usage avec un autre.
        await SendLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var answerToTheBackupCodeOfTheUser = await SendTwoFactorLoginRequestToTheApiAsync(clientThatKeepsTheCookiesOfTheBrowser, backupCodesGivenToTheUserAtActivation[0].GetString()!);
        Assert.Equal(HttpStatusCode.OK, answerToTheBackupCodeOfTheUser.StatusCode);

        var csrfTokenOfTheSecondSession = await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var answerToTheDeactivationOfTheTwoFactor = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheTwoFactorDeactivationEndpoint, new { password = ValidPasswordChosenInTheseTests, code = backupCodesGivenToTheUserAtActivation[1].GetString() }, csrfTokenOfTheSecondSession);
        Assert.Equal(HttpStatusCode.OK, answerToTheDeactivationOfTheTwoFactor.StatusCode);

        // Le nouveau téléphone : un nouveau secret et 10 nouveaux codes.
        WaitForTheNextCodeOnThePhone(apiStartedInMemoryForThisTest);
        var secretOfTheSecondPhoneOfTheUser = await StartActivationThenReadTheSecretAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheSecondSession);
        var answerToTheSecondActivationOfTheAccount = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, CreateBodyWithTheCodeOfThisSecret(apiStartedInMemoryForThisTest, secretOfTheSecondPhoneOfTheUser), csrfTokenOfTheSecondSession);
        Assert.Equal(HttpStatusCode.OK, answerToTheSecondActivationOfTheAccount.StatusCode);
        Assert.NotEqual(secretOfTheFirstPhoneOfTheUser, secretOfTheSecondPhoneOfTheUser);
        Assert.True(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
        Assert.Equal(10, apiStartedInMemoryForThisTest.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes.Count);

        Assert.Equal(1, CountEventsOfTheFakeJournal(apiStartedInMemoryForThisTest, SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Success));
        Assert.Equal(2, CountEventsOfTheFakeJournal(apiStartedInMemoryForThisTest, SecurityEventType.TwoFactorActivation, SecurityEventResult.Success));
    }

    // SS §11 : le journal note l'adresse IP de chaque requête de réglage, pour un refus comme pour une réussite.
    [Fact]
    public async Task JournalKeepsTheIpAddressOfEverySettingsRequest()
    {
        using var apiStartedInMemoryForThisTest = new ApiFactoryWithFakeRepositoryAndEmails();
        AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedInMemoryForThisTest);
        clientThatKeepsTheCookiesOfTheBrowser.DefaultRequestHeaders.Add(StartupFilterThatReadsTheVisitorAddress.NameOfTheHeaderGivingTheVisitorAddress, "203.0.113.7");
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var numberOfEventsBeforeTheSettingsRequests = apiStartedInMemoryForThisTest.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents.Count;

        await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationStartEndpoint, new { password = WrongPasswordTypedInTheLoginTests }, csrfTokenOfTheLoggedInUser);
        var secretOfThePhoneGivenByTheApi = await StartActivationThenReadTheSecretAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);
        await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, new { code = FindCodeThatIsWrongNow(apiStartedInMemoryForThisTest, secretOfThePhoneGivenByTheApi) }, csrfTokenOfTheLoggedInUser);
        await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, CreateBodyWithTheCodeOfThisSecret(apiStartedInMemoryForThisTest, secretOfThePhoneGivenByTheApi), csrfTokenOfTheLoggedInUser);
        await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheTwoFactorDeactivationEndpoint, new { password = WrongPasswordTypedInTheLoginTests, code = "123456" }, csrfTokenOfTheLoggedInUser);

        var securityEventsSavedByTheseRequests = apiStartedInMemoryForThisTest.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents.Skip(numberOfEventsBeforeTheSettingsRequests).ToList();
        Assert.Equal(4, securityEventsSavedByTheseRequests.Count);
        foreach (var securityEventSavedByOneRequest in securityEventsSavedByTheseRequests)
        {
            Assert.Equal(IPAddress.Parse("203.0.113.7"), securityEventSavedByOneRequest.IpAddress);
        }
    }

    // Compte les événements de ce type et de ce résultat dans le faux journal de l'API.
    private static int CountEventsOfTheFakeJournal(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest, SecurityEventType typeOfTheEventsToCount, SecurityEventResult resultOfTheEventsToCount)
    {
        var numberOfEventsFoundInTheJournal = 0;

        foreach (var securityEventKeptInTheJournal in apiStartedInMemoryForThisTest.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents)
        {
            if (securityEventKeptInTheJournal.Type == typeOfTheEventsToCount && securityEventKeptInTheJournal.Result == resultOfTheEventsToCount)
            {
                numberOfEventsFoundInTheJournal = numberOfEventsFoundInTheJournal + 1;
            }
        }

        return numberOfEventsFoundInTheJournal;
    }

    // --- Le journal technique

    // Au niveau le plus détaillé, le journal technique ne contient jamais le secret, le code ni un code de secours (SS §7).
    [Fact]
    public async Task TechnicalLogNeverContainsTheSecretOrTheCodesOfTheActivation()
    {
        using var apiWithTheMostDetailedLog = new ApiFactoryLoggingEverything();
        AddAccountToTheFakeRepositoryOfTheApi(apiWithTheMostDetailedLog);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiWithTheMostDetailedLog);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var secretOfThePhoneGivenByTheApi = await StartActivationThenReadTheSecretAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);
        var firstCodeTypedByTheUserAtActivation = CalculateCodeShownOnThePhoneNow(apiWithTheMostDetailedLog, secretOfThePhoneGivenByTheApi);

        var answerToTheActivationOfTheTwoFactor = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, new { code = firstCodeTypedByTheUserAtActivation }, csrfTokenOfTheLoggedInUser);
        var backupCodesGivenToTheUserAtActivation = JsonDocument.Parse(await answerToTheActivationOfTheTwoFactor.Content.ReadAsStringAsync()).RootElement.GetProperty("backupCodes");

        var textOfTheWholeTechnicalLog = string.Join("\n", apiWithTheMostDetailedLog.LoggerThatKeepsTheMessagesInMemory.MessagesWrittenInTheLog);
        Assert.NotEmpty(textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(secretOfThePhoneGivenByTheApi, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(firstCodeTypedByTheUserAtActivation, textOfTheWholeTechnicalLog);
        Assert.DoesNotContain(ValidPasswordChosenInTheseTests, textOfTheWholeTechnicalLog);
        foreach (var backupCodeGivenToTheUserAtActivation in backupCodesGivenToTheUserAtActivation.EnumerateArray())
        {
            Assert.DoesNotContain(backupCodeGivenToTheUserAtActivation.GetString()!, textOfTheWholeTechnicalLog);
        }
    }

    // Une panne au moment d'activer répond 500, sans détail, et n'active rien (SS §10).
    [Fact]
    public async Task DatabaseFailureDuringTheActivationAnswersAGenericErrorInProduction()
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var accountUsedByThisTwoFactorTest = AddAccountToTheFakeRepositoryOfTheApi(apiStartedLikeInProductionForThisTest);
        var clientThatKeepsTheCookiesOfTheBrowser = CreateClientThatKeepsCookiesLikeABrowser(apiStartedLikeInProductionForThisTest);
        var csrfTokenOfTheLoggedInUser = await LogInWithPasswordThenAskForTheCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser);
        var secretOfThePhoneGivenByTheApi = await StartActivationThenReadTheSecretAsync(clientThatKeepsTheCookiesOfTheBrowser, csrfTokenOfTheLoggedInUser);
        apiStartedLikeInProductionForThisTest.RepositoryThatKeepsTwoFactorDataInMemory.DatabaseIsDownWhileSavingTheSettings = true;

        var answerGivenDuringTheSimulatedFailure = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatKeepsTheCookiesOfTheBrowser, AddressOfTheActivationFinishEndpoint, CreateBodyWithTheCodeOfThisSecret(apiStartedLikeInProductionForThisTest, secretOfThePhoneGivenByTheApi), csrfTokenOfTheLoggedInUser);

        await CheckThatAnswerIsProblemWithThisExactTitleAsync(answerGivenDuringTheSimulatedFailure, HttpStatusCode.InternalServerError, "Une erreur est survenue. Réessaie plus tard.");
        Assert.DoesNotContain(FakeTwoFactorAuthenticationRepository.TechnicalDetailOfTheSimulatedTwoFactorFailure, await answerGivenDuringTheSimulatedFailure.Content.ReadAsStringAsync());
        Assert.False(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
    }
}
