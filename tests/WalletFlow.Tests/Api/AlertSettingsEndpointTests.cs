using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes des réglages d'alerte (GR Notif, SS §4 et §6) dans la vraie API, avec une fausse base.
public class AlertSettingsEndpointTests
{
    private const string SettingsEndpoint = "/api/alert-settings";
    private const string ChannelEndpoint = SettingsEndpoint + "/channel";

    // Un utilisateur connecté : son navigateur, son jeton anti-CSRF et son compte.
    private sealed record LoggedInUser(HttpClient Client, string CsrfToken, User Account);

    private static async Task<LoggedInUser> LogInAsync(ApiFactoryWithFakes api)
    {
        var account = await AddAccountAsync(api, Email);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client, Email);

        return new LoggedInUser(client, csrfToken, account);
    }

    // Adresse du réglage de ce type d'alerte.
    private static string AlertAddress(string type)
    {
        return SettingsEndpoint + "/alerts/" + type;
    }

    // Envoie ce corps en PUT, avec ce jeton anti-CSRF si le test en donne.
    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string address, object? body, string? csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, address);

        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (csrfToken != null)
        {
            request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);
        }

        return await client.SendAsync(request);
    }

    // Envoie ce texte JSON tel quel : certains corps ne s'écrivent pas avec un objet C#.
    private static async Task<HttpResponseMessage> PutRawAsync(LoggedInUser user, string address, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, address)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, user.CsrfToken);

        return await user.Client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Le choix de ce type dans des réglages lus.
    private static bool IsEnabledIn(JsonElement settings, string type)
    {
        return settings.GetProperty("alerts").EnumerateArray()
            .Single(alert => alert.GetProperty("type").GetString() == type)
            .GetProperty("isEnabled").GetBoolean();
    }

    [Fact]
    public async Task EverySettingsRouteNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(SettingsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAsync(client, ChannelEndpoint, new { channel = "phone" }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAsync(client, AlertAddress("budgetReached"), new { isEnabled = false }, null)).StatusCode);
    }

    // SS §6 : sans le jeton anti-CSRF, rien ne change.
    [Fact]
    public async Task WriteRoutesWithoutCsrfTokenChangeNothing()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var channel = await PutAsync(user.Client, ChannelEndpoint, new { channel = "phone" }, null);
        var alert = await PutAsync(user.Client, AlertAddress("budgetReached"), new { isEnabled = false }, null);

        Assert.Equal(HttpStatusCode.BadRequest, channel.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, alert.StatusCode);
        Assert.Equal(NotificationChannel.Email, user.Account.NotificationChannel);
        Assert.Empty(api.AlertRepository.SavedPreferences);
    }

    // Les 8 types, dans l'ordre de la liste, avec leur nom pour le site, leur libellé et leur choix. Ce sont des données
    // personnelles : aucun navigateur ni intermédiaire ne doit les garder.
    [Fact]
    public async Task SettingsGiveTheEightTypesAndAreNeverCached()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await user.Client.GetAsync(SettingsEndpoint);
        var settings = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("email", settings.GetProperty("channel").GetString());
        var alerts = settings.GetProperty("alerts").EnumerateArray().ToList();
        Assert.Equal(
            new List<string?>
            {
                "budgetReached", "newSuggestion", "bagGoalReached", "bankAuthorizationToRenew",
                "newDevice", "accountLocked", "passwordChange", "emailChange"
            },
            alerts.Select(alert => alert.GetProperty("type").GetString()).ToList());
        Assert.Equal(
            new List<bool> { true, true, true, true, false, false, false, false },
            alerts.Select(alert => alert.GetProperty("canBeDisabled").GetBoolean()).ToList());
        Assert.All(alerts, alert => Assert.True(alert.GetProperty("isEnabled").GetBoolean()));
        Assert.Equal("Objectif d'un sac atteint", alerts[2].GetProperty("label").GetString());
    }

    // Couper une alerte répond avec les réglages à jour, et la remettre aussi.
    [Fact]
    public async Task AlertIsSwitchedOffThenOn()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var off = await PutAsync(user.Client, AlertAddress("bagGoalReached"), new { isEnabled = false }, user.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False(IsEnabledIn(await ReadJsonAsync(off), "bagGoalReached"));
        Assert.False(Assert.Single(api.AlertRepository.SavedPreferences).IsEnabled);

        var on = await PutAsync(user.Client, AlertAddress("bagGoalReached"), new { isEnabled = true }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        Assert.True(IsEnabledIn(await ReadJsonAsync(on), "bagGoalReached"));
        Assert.True(Assert.Single(api.AlertRepository.SavedPreferences).IsEnabled);
        Assert.Equal(AlertKind.BagGoalReached, api.AlertRepository.SavedPreferences[0].Kind);
        Assert.Equal(user.Account.Id, api.AlertRepository.SavedPreferences[0].UserId);
    }

    // GR Notif : une alerte de sécurité ne se désactive pas. La réponse le dit, et rien n'est enregistré.
    [Theory]
    [InlineData("newDevice")]
    [InlineData("accountLocked")]
    [InlineData("passwordChange")]
    [InlineData("emailChange")]
    public async Task SecurityAlertCannotBeDisabled(string type)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await PutAsync(user.Client, AlertAddress(type), new { isEnabled = false }, user.CsrfToken);

        await AssertProblemTitleAsync(response, HttpStatusCode.Conflict, ErrorMessages.SecurityAlertCannotBeDisabled);
        Assert.Empty(api.AlertRepository.SavedPreferences);
    }

    // Un type inconnu, ou écrit avec d'autres majuscules, répond comme une adresse qui n'existe pas.
    [Theory]
    [InlineData("budget_atteint")]
    [InlineData("BudgetReached")]
    [InlineData("rien")]
    public async Task UnknownTypeIsNotFound(string type)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await PutAsync(user.Client, AlertAddress(type), new { isEnabled = false }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(api.AlertRepository.SavedPreferences);
    }

    // Le choix doit être écrit : une requête vide, ou un texte à la place du vrai ou faux, ne coupe jamais une alerte.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"isEnabled\":null}")]
    [InlineData("{\"isEnabled\":\"false\"}")]
    [InlineData("{\"isEnabled\":0}")]
    public async Task MissingOrMalformedChoiceIsRefused(string json)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await PutRawAsync(user, AlertAddress("budgetReached"), json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.AlertRepository.SavedPreferences);
    }

    // GR Notif : le choix du téléphone est gardé pour plus tard. Chaque canal de la liste est accepté.
    [Theory]
    [InlineData("phone", NotificationChannel.Phone)]
    [InlineData("emailAndPhone", NotificationChannel.EmailAndPhone)]
    [InlineData("email", NotificationChannel.Email)]
    public async Task ChosenChannelIsKept(string channel, NotificationChannel expectedChannel)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await PutAsync(user.Client, ChannelEndpoint, new { channel }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(channel, (await ReadJsonAsync(response)).GetProperty("channel").GetString());
        Assert.Equal(expectedChannel, user.Account.NotificationChannel);
    }

    // Un canal hors de la liste est refusé sur son champ, et le canal ne change pas.
    [Theory]
    [InlineData("{\"channel\":\"sms\"}")]
    [InlineData("{\"channel\":\"Phone\"}")]
    [InlineData("{\"channel\":\"telephone\"}")]
    [InlineData("{\"channel\":null}")]
    [InlineData("{}")]
    public async Task ChannelOutsideTheListIsRefused(string json)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await PutRawAsync(user, ChannelEndpoint, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(ErrorMessages.AlertChannelUnknown, body.GetProperty("errors").GetProperty("Channel")[0].GetString());
        Assert.Equal(NotificationChannel.Email, user.Account.NotificationChannel);
    }

    // Les réglages d'un utilisateur ne touchent jamais ceux d'un autre : chaque route ne prend que le compte de la session.
    [Fact]
    public async Task ChoiceOfOneUserLeavesTheOtherUnchanged()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        var otherAccount = await AddAccountAsync(api, "autre-reglages@exemple.test");
        var otherClient = CreateBrowserClient(api);
        await LogInThenGetCsrfTokenAsync(otherClient, "autre-reglages@exemple.test");

        await PutAsync(user.Client, AlertAddress("newSuggestion"), new { isEnabled = false }, user.CsrfToken);
        await PutAsync(user.Client, ChannelEndpoint, new { channel = "phone" }, user.CsrfToken);
        var otherSettings = await ReadJsonAsync(await otherClient.GetAsync(SettingsEndpoint));

        Assert.True(IsEnabledIn(otherSettings, "newSuggestion"));
        Assert.Equal("email", otherSettings.GetProperty("channel").GetString());
        Assert.Equal(NotificationChannel.Email, otherAccount.NotificationChannel);
    }
}
