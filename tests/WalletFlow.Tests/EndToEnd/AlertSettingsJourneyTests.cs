using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;
using static WalletFlow.Tests.EndToEnd.RealAccountSteps;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt les réglages d'alerte comme un utilisateur, sur la vraie base de test (GR Notif) : la table parametrer, ses codes
// de type, sa clé et le déclencheur qui protège les alertes de sécurité sont les vrais.
[Collection(RealDatabaseCollection.Name)]
public class AlertSettingsJourneyTests
{
    private const string SettingsEndpoint = "/api/alert-settings";

    // Envoie ce corps en PUT avec le jeton anti-CSRF.
    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string address, object body, string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, address) { Content = JsonContent.Create(body) };
        request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);

        return await client.SendAsync(request);
    }

    // Lit le choix de ce type dans les réglages que donne l'API.
    private static async Task<bool> ReadIsEnabledAsync(HttpClient client, string type)
    {
        var settings = JsonDocument.Parse(await (await client.GetAsync(SettingsEndpoint)).Content.ReadAsStringAsync()).RootElement;

        return settings.GetProperty("alerts").EnumerateArray()
            .Single(alert => alert.GetProperty("type").GetString() == type)
            .GetProperty("isEnabled").GetBoolean();
    }

    [Fact]
    public async Task UserSwitchesAlertsAndChoosesTheChannelThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("parcours-reglages");
        var browser = CreateBrowser(api);

        try
        {
            await RegisterThenConfirmAsync(api, browser, email);
            var csrfToken = await LogInThenGetCsrfTokenAsync(browser, email);
            Assert.True(await ReadIsEnabledAsync(browser, "budgetReached"));

            Assert.Equal(HttpStatusCode.OK, (await PutAsync(browser, SettingsEndpoint + "/alerts/budgetReached", new { isEnabled = false }, csrfToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PutAsync(browser, SettingsEndpoint + "/alerts/bankAuthorizationToRenew", new { isEnabled = false }, csrfToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PutAsync(browser, SettingsEndpoint + "/alerts/bankAuthorizationToRenew", new { isEnabled = true }, csrfToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await PutAsync(browser, SettingsEndpoint + "/alerts/passwordChange", new { isEnabled = false }, csrfToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PutAsync(browser, SettingsEndpoint + "/channel", new { channel = "emailAndPhone" }, csrfToken)).StatusCode);

            Assert.False(await ReadIsEnabledAsync(browser, "budgetReached"));
            Assert.True(await ReadIsEnabledAsync(browser, "bankAuthorizationToRenew"));
            Assert.True(await ReadIsEnabledAsync(browser, "passwordChange"));

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var account = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Email == email);
            var preferences = await databaseContext.AlertPreferences.AsNoTracking().Where(row => row.UserId == account.Id).ToListAsync();
            Assert.Equal(NotificationChannel.EmailAndPhone, account.NotificationChannel);
            Assert.Equal(
                new HashSet<(AlertKind, bool)> { (AlertKind.BudgetReached, false), (AlertKind.BankAuthorizationToRenew, true) },
                preferences.Select(row => (row.Kind, row.IsEnabled)).ToHashSet());
            Assert.Equal(
                "les_deux",
                await databaseContext.Database
                    .SqlQuery<string>($"SELECT canal_notification AS \"Value\" FROM utilisateur WHERE id_utilisateur = {account.Id}")
                    .SingleAsync());
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
