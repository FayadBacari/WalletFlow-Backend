using System.Globalization;
using System.Net;
using System.Text.Json;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la route de la demande de suppression (GR Suppression, SS §2, §6 et §7) dans la vraie API, avec une fausse base :
// le mot de passe, la session fermée et son cookie effacé, le jour de l'effacement, la limite de 10 par heure, et la connexion
// qui annule la demande en le disant.
public class AccountDeletionEndpointTests
{
    private const string DeletionEndpoint = "/api/account/delete";

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Le bon mot de passe demande la suppression : la réponse donne le jour de Paris de l'effacement, toutes les sessions du compte
    // sont fermées, et le cookie de la session en cours est effacé.
    [Fact]
    public async Task RightPasswordRequestsTheDeletionAndClosesTheSession()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        var response = await SendSettingsRequestAsync(client, DeletionEndpoint, new { password = ValidPassword }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = await ReadJsonAsync(response);
        var erasureDay = UsersCalendar.FindToday(api.Clock.GetUtcNow().AddDays(30)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal(
            (AccountDeletionController.DeletionRequestedMessage, erasureDay),
            (answer.GetProperty("message").GetString(), answer.GetProperty("erasureDate").GetString()));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.StartsWith(SessionCookiesConfiguration.SessionCookieName + "=;", StringComparison.Ordinal) && cookie.Contains("expires=Thu, 01 Jan 1970"));
        Assert.DoesNotContain(api.SessionRepository.SavedSessions, session => session.UserId == account.Id);
        Assert.Equal(api.Clock.GetUtcNow().UtcDateTime, account.DeletionRequestedAt);
    }

    // Le jour de l'effacement est celui de Paris : à 23 h 30 en UTC, Paris a déjà changé de jour.
    [Fact]
    public async Task ErasureDayIsTheDayOfParis()
    {
        using var api = new ApiFactoryWithFakes();
        api.Clock.Advance(TimeSpan.FromHours(11.5));
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        var answer = await ReadJsonAsync(await SendSettingsRequestAsync(client, DeletionEndpoint, new { password = ValidPassword }, csrfToken));

        Assert.Equal(new DateTimeOffset(2026, 11, 4, 23, 30, 0, TimeSpan.Zero), api.Clock.GetUtcNow());
        Assert.Equal("2026-12-05", answer.GetProperty("erasureDate").GetString());
    }

    // Un compte effacé entre la lecture de la session et la demande reçoit la réponse d'une session finie : il n'y a plus rien
    // à supprimer.
    [Fact]
    public async Task AccountGoneSinceTheSessionGetsTheLoginRequiredAnswer()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);
        api.AccountRepository.SavedUsers.Remove(account);

        var response = await SendSettingsRequestAsync(client, DeletionEndpoint, new { password = ValidPassword }, csrfToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(UserSessionAuthenticationHandler.LoginRequiredMessage, (await ReadJsonAsync(response)).GetProperty("title").GetString());
    }

    // GR Suppression : se reconnecter pendant les 30 jours annule la demande, et la réponse le dit. Une connexion ordinaire ne dit
    // rien de tel.
    [Fact]
    public async Task LoginAfterTheRequestCancelsItAndSaysSo()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);
        await SendSettingsRequestAsync(client, DeletionEndpoint, new { password = ValidPassword }, csrfToken);

        var cancellingLogin = await ReadJsonAsync(await SendLoginRequestAsync(client, Email, ValidPassword));
        var ordinaryLogin = await ReadJsonAsync(await SendLoginRequestAsync(client, Email, ValidPassword));

        Assert.Equal(
            (AccountLoginController.DeletionCancelledMessage, true),
            (cancellingLogin.GetProperty("message").GetString(), cancellingLogin.GetProperty("isDeletionCancelled").GetBoolean()));
        Assert.Equal(
            (AccountLoginController.LoggedInMessage, false),
            (ordinaryLogin.GetProperty("message").GetString(), ordinaryLogin.GetProperty("isDeletionCancelled").GetBoolean()));
        Assert.Null(account.DeletionRequestedAt);
    }

    // Avec la double authentification, la demande se fait avec le mot de passe et un code, et c'est la connexion finie avec le
    // code suivant qui annule la demande, en le disant.
    [Fact]
    public async Task TwoFactorLoginAfterTheRequestCancelsItAndSaysSo()
    {
        using var api = new ApiFactoryWithFakes();
        var activated = await AddActivatedAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInWithCodeThenGetCsrfTokenAsync(api, client, activated.Secret);
        WaitForNextCode(api);
        var requested = await SendSettingsRequestAsync(
            client, DeletionEndpoint, new { password = ValidPassword, code = CalculateCurrentCode(api, activated.Secret) }, csrfToken);
        WaitForNextCode(api);

        await SendLoginRequestAsync(client, Email, ValidPassword);
        var cancellingLogin = await ReadJsonAsync(await SendTwoFactorLoginRequestAsync(client, CalculateCurrentCode(api, activated.Secret)));

        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        Assert.Equal(
            (AccountLoginController.DeletionCancelledMessage, true),
            (cancellingLogin.GetProperty("message").GetString(), cancellingLogin.GetProperty("isDeletionCancelled").GetBoolean()));
        Assert.Null(activated.UserAccount.DeletionRequestedAt);
    }

    // Un mauvais mot de passe est refusé sur son champ, et le compte reste tel quel.
    [Fact]
    public async Task WrongPasswordIsRefusedOnItsField()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        var response = await SendSettingsRequestAsync(client, DeletionEndpoint, new { password = WrongPassword }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorMessages.WrongCurrentPassword,
            (await ReadJsonAsync(response)).GetProperty("errors").GetProperty(nameof(AccountDeletionRequest.Password))[0].GetString());
        Assert.Null(account.DeletionRequestedAt);
        Assert.Contains(api.SessionRepository.SavedSessions, session => session.UserId == account.Id);
    }

    // Avec la double authentification, sans le code, le refus ne dit pas lequel des deux est faux.
    [Fact]
    public async Task TwoFactorAccountWithoutItsCodeGetsTheSharedRefusal()
    {
        using var api = new ApiFactoryWithFakes();
        var activated = await AddActivatedAccountAsync(api);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInWithCodeThenGetCsrfTokenAsync(api, client, activated.Secret);

        var response = await SendSettingsRequestAsync(client, DeletionEndpoint, new { password = ValidPassword }, csrfToken);

        Assert.Equal(
            ErrorMessages.WrongPasswordOrCode,
            (await ReadJsonAsync(response)).GetProperty("errors").GetProperty(ApiErrorConfiguration.GeneralErrorsKey)[0].GetString());
        Assert.Null(activated.UserAccount.DeletionRequestedAt);
    }

    // SS §6 : sans le jeton anti-CSRF, la demande est refusée et rien ne change.
    [Fact]
    public async Task DeletionNeedsTheCsrfToken()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        await LogInThenGetCsrfTokenAsync(client);

        var response = await SendSettingsRequestAsync(client, DeletionEndpoint, new { password = ValidPassword }, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(account.DeletionRequestedAt);
    }

    // SS §6 : 10 demandes par heure et par adresse IP ; la 11e reçoit « trop de requêtes », même avec le bon mot de passe.
    [Fact]
    public async Task EleventhRequestFromTheSameAddressIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        client.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, "203.0.113.90");
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SendSettingsRequestAsync(client, DeletionEndpoint, new { password = WrongPassword }, csrfToken)).StatusCode);
        }

        var eleventh = await SendSettingsRequestAsync(client, DeletionEndpoint, new { password = ValidPassword }, csrfToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.Null(account.DeletionRequestedAt);
    }

    // Le test ne peut pas attendre une heure : il vérifie la durée exacte de la limite (SS §6), que la suppression partage avec
    // le changement de mot de passe.
    [Fact]
    public void DeletionLimitLastsExactlyOneHour()
    {
        var deletionLimit = RateLimitingConfiguration.CreatePasswordChangeLimit();

        Assert.Equal((10, TimeSpan.FromHours(1)), (deletionLimit.PermitLimit, deletionLimit.Window));
    }
}
