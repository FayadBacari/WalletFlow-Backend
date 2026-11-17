using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;
using static WalletFlow.Tests.EndToEnd.RealAccountSteps;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt le téléchargement des données comme un utilisateur, sur la vraie API et la vraie base de test (GR Compte, SS §6) :
// le vrai dépôt, sa transaction en lecture seule, et le vrai fichier JSON.
[Collection(RealDatabaseCollection.Name)]
public class DataExportJourneyTests
{
    // L'utilisateur crée une catégorie et donne son prénom, puis télécharge ses données : elles y sont, avec ses réglages
    // d'alerte, et l'empreinte de son mot de passe n'y est pas. Le téléchargement est noté au journal.
    [Fact]
    public async Task UserDownloadsWhatTheAccountHoldsThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("parcours-export");
        var browser = CreateBrowser(api);

        try
        {
            await RegisterThenConfirmAsync(api, browser, email);
            var csrfToken = await LogInThenGetCsrfTokenAsync(browser, email);
            Assert.Equal(HttpStatusCode.Created, (await SendSettingsRequestAsync(browser, "/api/categories", new { name = "Jardinage" }, csrfToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendSettingsRequestAsync(browser, "/api/account/name", new { lastName = "Dupont", firstName = "Élise" }, csrfToken)).StatusCode);

            var response = await SendSettingsRequestAsync(browser, "/api/account/export", new { password = ValidPassword }, csrfToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            var file = JsonDocument.Parse(text).RootElement;
            var profile = file.GetProperty("profile");
            Assert.Equal((email, "Dupont", "Élise"), (profile.GetProperty("email").GetString(), profile.GetProperty("lastName").GetString(), profile.GetProperty("firstName").GetString()));
            var categoryNames = file.GetProperty("categories").EnumerateArray().Select(category => category.GetProperty("name").GetString()).ToList();
            Assert.Equal(9, categoryNames.Count);
            Assert.Contains("Jardinage", categoryNames);
            Assert.Equal(10, file.GetProperty("alertSettings").GetArrayLength());
            Assert.Equal(0, file.GetProperty("transactions").GetArrayLength());

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var account = await databaseContext.Users.SingleAsync(user => user.Email == email);
            Assert.DoesNotContain(account.PasswordHash, text);
            Assert.Equal(1, await databaseContext.SecurityEvents.CountAsync(securityEvent =>
                securityEvent.UserId == account.Id
                && securityEvent.Type == SecurityEventType.DataExport
                && securityEvent.Result == SecurityEventResult.Success));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
