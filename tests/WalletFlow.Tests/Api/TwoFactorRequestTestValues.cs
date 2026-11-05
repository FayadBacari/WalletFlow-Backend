using System.Net.Http.Json;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Valeurs et outils communs aux tests des routes de la double authentification.
public static class TwoFactorRequestTestValues
{
    public const string AddressOfTheTwoFactorLoginEndpoint = "/api/account/login/two-factor";
    public const string AddressOfTheActivationStartEndpoint = "/api/account/two-factor/start-activation";
    public const string AddressOfTheActivationFinishEndpoint = "/api/account/two-factor/finish-activation";
    public const string AddressOfTheTwoFactorDeactivationEndpoint = "/api/account/two-factor/deactivate";
    public const string NameOfTheWaitingLoginCookieInTheseTests = "__Host-walletflow-two-factor";

    // Ajoute à la fausse base un compte déjà activé, avec le vrai Argon2. Renvoie le secret de son téléphone.
    // Un seul code de secours, avec le vrai Argon2 aussi : 10 ralentiraient chaque test sans rien vérifier de plus.
    public static ActivatedApiAccountOfTheseTests AddActivatedAccountToTheFakeRepositoryOfTheApi(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest, string emailAddressOfTheAccountToAdd = EmailAddressOfTheLoginTests)
    {
        var accountAddedToTheFakeRepository = AddAccountToTheFakeRepositoryOfTheApi(apiStartedInMemoryForThisTest, emailAddressOfTheAccountToAdd);
        var secretSharedWithTheAuthenticatorApplication = new OtpNetAuthenticatorCodeTool().GenerateNewRandomSecretForTheAuthenticatorApplication();
        var backupCodeInItsCleanForm = new RandomSecureTokenGenerator().GenerateNewRandomBackupCodeInItsCleanForm();

        accountAddedToTheFakeRepository.IsTwoFactorEnabled = true;
        accountAddedToTheFakeRepository.EncryptedTotpSecret = new AesGcmTotpSecretProtector(apiStartedInMemoryForThisTest.EncryptionKeyOfTheTotpSecretsInTheseTests).EncryptSecretOfThisUserAccount(secretSharedWithTheAuthenticatorApplication, accountAddedToTheFakeRepository.Id);
        apiStartedInMemoryForThisTest.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes.Add(new BackupCode
        {
            CodeHash = new LibsodiumPasswordHasher().ConvertPasswordIntoItsSecureHash(backupCodeInItsCleanForm),
            UserId = accountAddedToTheFakeRepository.Id
        });

        return new ActivatedApiAccountOfTheseTests
        {
            UserAccount = accountAddedToTheFakeRepository,
            SecretOfTheAuthenticatorApplication = secretSharedWithTheAuthenticatorApplication,
            BackupCodeInItsCleanForm = backupCodeInItsCleanForm
        };
    }

    // Calcule le code qu'affiche le téléphone à la date de la fausse horloge de l'API.
    public static string CalculateCodeShownOnThePhoneNow(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest, string secretSharedWithTheAuthenticatorApplication)
    {
        return TwoFactorTestTools.CalculateAuthenticatorCodeShownAtThisDate(secretSharedWithTheAuthenticatorApplication, apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow());
    }

    // Donne un code à 6 chiffres qui ne vaut rien à la date de la fausse horloge de l'API.
    public static string FindCodeThatIsWrongNow(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest, string secretSharedWithTheAuthenticatorApplication)
    {
        return TwoFactorTestTools.FindAuthenticatorCodeThatIsWrongAtThisDate(secretSharedWithTheAuthenticatorApplication, apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow());
    }

    // Passe à la période suivante : le téléphone affiche un nouveau code.
    public static void WaitForTheNextCodeOnThePhone(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest)
    {
        apiStartedInMemoryForThisTest.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromSeconds(TwoFactorTestTools.NumberOfSecondsInOneAuthenticatorPeriod));
    }

    // Envoie un code pour finir la connexion.
    public static async Task<HttpResponseMessage> SendTwoFactorLoginRequestToTheApiAsync(HttpClient clientThatCallsTheApiInMemory, string codeTypedByTheVisitorInThisTest)
    {
        return await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheTwoFactorLoginEndpoint, new { code = codeTypedByTheVisitorInThisTest });
    }

    // Se connecte complètement, avec le mot de passe puis le code, et renvoie le jeton anti-CSRF de la session.
    public static async Task<string> LogInWithPasswordAndCodeThenAskForTheCsrfTokenAsync(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest, HttpClient clientThatCallsTheApiInMemory, string secretSharedWithTheAuthenticatorApplication)
    {
        await SendLoginRequestToTheApiAsync(clientThatCallsTheApiInMemory);
        await SendTwoFactorLoginRequestToTheApiAsync(clientThatCallsTheApiInMemory, CalculateCodeShownOnThePhoneNow(apiStartedInMemoryForThisTest, secretSharedWithTheAuthenticatorApplication));

        return await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatCallsTheApiInMemory);
    }

    // Se connecte avec le mot de passe seul, et renvoie le jeton anti-CSRF de la session.
    public static async Task<string> LogInWithPasswordThenAskForTheCsrfTokenAsync(HttpClient clientThatCallsTheApiInMemory, string emailAddressTypedInThisTest = EmailAddressOfTheLoginTests)
    {
        await SendLoginRequestToTheApiAsync(clientThatCallsTheApiInMemory, emailAddressTypedInThisTest);

        return await AskForTheCsrfTokenOfTheLoggedInUserAsync(clientThatCallsTheApiInMemory);
    }

    // Envoie à une route de réglage ce corps de requête, avec ce jeton anti-CSRF si le test en donne un.
    public static async Task<HttpResponseMessage> SendSettingsRequestWithThisCsrfTokenAsync(HttpClient clientThatCallsTheApiInMemory, string addressOfTheSettingsEndpointBeingTested, object bodySentToTheSettingsRouteOfTheApi, string? csrfTokenSentWithTheRequest)
    {
        var settingsRequestToSendToTheApi = new HttpRequestMessage(HttpMethod.Post, addressOfTheSettingsEndpointBeingTested) { Content = JsonContent.Create(bodySentToTheSettingsRouteOfTheApi) };

        if (csrfTokenSentWithTheRequest != null)
        {
            settingsRequestToSendToTheApi.Headers.Add(AntiforgeryConfiguration.NameOfTheHeaderCarryingTheCsrfToken, csrfTokenSentWithTheRequest);
        }

        return await clientThatCallsTheApiInMemory.SendAsync(settingsRequestToSendToTheApi);
    }

    // Commence l'activation avec le mot de passe des tests, et renvoie le secret donné par l'API.
    public static async Task<string> StartActivationThenReadTheSecretAsync(HttpClient clientThatCallsTheApiInMemory, string csrfTokenOfTheLoggedInUser)
    {
        var answerToTheStartOfTheActivation = await SendSettingsRequestWithThisCsrfTokenAsync(clientThatCallsTheApiInMemory, AddressOfTheActivationStartEndpoint, new { password = ValidPasswordChosenInTheseTests }, csrfTokenOfTheLoggedInUser);
        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheStartOfTheActivation.Content.ReadAsStringAsync());

        return jsonDocumentOfTheApiAnswer.RootElement.GetProperty("secret").GetString()!;
    }
}

// Un compte activé dans la fausse base de l'API, avec ce que son propriétaire connaît.
public class ActivatedApiAccountOfTheseTests
{
    public required User UserAccount { get; init; }
    public required string SecretOfTheAuthenticatorApplication { get; init; }
    public required string BackupCodeInItsCleanForm { get; init; }
}
