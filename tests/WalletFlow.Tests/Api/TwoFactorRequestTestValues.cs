using System.Net.Http.Json;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Valeurs et outils communs aux tests des routes de la double authentification.
public static class TwoFactorRequestTestValues
{
    public const string TwoFactorLoginEndpoint = "/api/account/login/two-factor";
    public const string ActivationStartEndpoint = "/api/account/two-factor/start-activation";
    public const string ActivationFinishEndpoint = "/api/account/two-factor/finish-activation";
    public const string DeactivationEndpoint = "/api/account/two-factor/deactivate";
    public const string WaitingLoginCookieName = "__Host-walletflow-two-factor";

    // Ajoute à la fausse base un compte déjà activé, avec le vrai Argon2. Renvoie le secret de son téléphone.
    // Un seul code de secours, avec le vrai Argon2 aussi : 10 ralentiraient chaque test sans rien vérifier de plus.
    public static async Task<ActivatedApiAccount> AddActivatedAccountAsync(ApiFactoryWithFakes api, string emailAddress = Email)
    {
        var account = await AddAccountAsync(api, emailAddress);
        var secret = new OtpNetAuthenticatorCodeTool().GenerateSecret();
        var cleanedCode = new RandomSecureTokenGenerator().GenerateBackupCode();

        account.ActivateTwoFactor(new AesGcmTotpSecretProtector(api.TotpEncryptionKey).Encrypt(secret, account.Id));
        api.TwoFactorRepository.SavedBackupCodes.Add(new BackupCode
        {
            CodeHash = await RealArgonHashers.CreateBackupCodeHasher().HashAsync(cleanedCode, account.Id),
            UserId = account.Id
        });

        return new ActivatedApiAccount
        {
            UserAccount = account,
            Secret = secret,
            BackupCode = cleanedCode
        };
    }

    // Calcule le code qu'affiche le téléphone à la date de la fausse horloge de l'API.
    public static string CalculateCurrentCode(ApiFactoryWithFakes api, string secret)
    {
        return TwoFactorTestTools.CalculateCodeAt(secret, api.Clock.GetUtcNow());
    }

    // Donne un code à 6 chiffres qui ne vaut rien à la date de la fausse horloge de l'API.
    public static string FindWrongCode(ApiFactoryWithFakes api, string secret)
    {
        return TwoFactorTestTools.FindWrongCodeAt(secret, api.Clock.GetUtcNow());
    }

    // Passe à la période suivante : le téléphone affiche un nouveau code.
    public static void WaitForNextCode(ApiFactoryWithFakes api)
    {
        api.Clock.Advance(TimeSpan.FromSeconds(TwoFactorTestTools.PeriodInSeconds));
    }

    // Envoie un code pour finir la connexion.
    public static async Task<HttpResponseMessage> SendTwoFactorLoginRequestAsync(HttpClient client, string code)
    {
        return await client.PostAsJsonAsync(TwoFactorLoginEndpoint, new { code = code });
    }

    // Se connecte complètement, avec le mot de passe puis le code, et renvoie le jeton anti-CSRF de la session.
    public static async Task<string> LogInWithCodeThenGetCsrfTokenAsync(ApiFactoryWithFakes api, HttpClient client, string secret)
    {
        await SendLoginRequestAsync(client);
        await SendTwoFactorLoginRequestAsync(client, CalculateCurrentCode(api, secret));

        return await GetCsrfTokenAsync(client);
    }

    // Se connecte avec le mot de passe seul, et renvoie le jeton anti-CSRF de la session.
    public static async Task<string> LogInThenGetCsrfTokenAsync(HttpClient client, string emailAddress = Email)
    {
        await SendLoginRequestAsync(client, emailAddress);

        return await GetCsrfTokenAsync(client);
    }

    // Envoie à une route de réglage ce corps de requête, avec ce jeton anti-CSRF si le test en donne un.
    public static async Task<HttpResponseMessage> SendSettingsRequestAsync(HttpClient client, string address, object body, string? csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(body) };

        if (csrfToken != null)
        {
            request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);
        }

        return await client.SendAsync(request);
    }

    // Commence l'activation avec le mot de passe des tests, et renvoie le secret donné par l'API.
    public static async Task<string> StartActivationThenReadSecretAsync(HttpClient client, string csrfToken)
    {
        var response = await SendSettingsRequestAsync(client, ActivationStartEndpoint, new { password = ValidPassword }, csrfToken);
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return jsonDocument.RootElement.GetProperty("secret").GetString()!;
    }
}

// Un compte activé dans la fausse base de l'API, avec ce que son propriétaire connaît.
public class ActivatedApiAccount
{
    public required User UserAccount { get; init; }
    public required string Secret { get; init; }
    public required string BackupCode { get; init; }
}
