using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt le compte et les données personnelles comme un utilisateur, sur la vraie base de test (GR Compte, SS §2 et §3) :
// les droits de la base sur chaque colonne, la file des e-mails et les transactions sont les vrais.
[Collection(RealDatabaseCollection.Name)]
public class AccountSettingsJourneyTests
{
    private const string ChangedPassword = "Nouveau-Mot-De-Passe-3";

    // Crée un client qui garde les cookies comme un navigateur, en HTTPS.
    private static HttpClient CreateBrowser(ApiFactoryWithRealDatabase api)
    {
        return api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    }

    // Inscrit puis confirme un compte avec cette adresse et le mot de passe des tests.
    private static async Task RegisterThenConfirmAsync(ApiFactoryWithRealDatabase api, HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RegistrationEndpoint, new { email = email, password = ValidPassword })).StatusCode);

        var confirmationEmail = api.EmailSender.SentEmails.Single(sentEmail => sentEmail.RecipientAddress == email);
        var confirmationResponse = await client.PostAsJsonAsync(
            ConfirmationEndpoint,
            new { token = ReadConfirmationToken(confirmationEmail.Body), password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, confirmationResponse.StatusCode);
    }

    // Lit le profil avec ce client.
    private static async Task<JsonElement> ReadProfileAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/account/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task UserChangesNameThenPasswordThenEmailThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var oldEmail = CreateUniqueEmail("parcours-compte");
        var newEmail = CreateUniqueEmail("parcours-nouvelle-adresse");
        var currentBrowser = CreateBrowser(api);
        var otherBrowser = CreateBrowser(api);

        try
        {
            await RegisterThenConfirmAsync(api, currentBrowser, oldEmail);
            var csrfToken = await LogInThenGetCsrfTokenAsync(currentBrowser, oldEmail);
            await LogInThenGetCsrfTokenAsync(otherBrowser, oldEmail);

            // Le nom et le prénom, sans mot de passe.
            var nameResponse = await SendSettingsRequestAsync(currentBrowser, "/api/account/name", new { lastName = "Dupont", firstName = "Élise" }, csrfToken);
            Assert.Equal(HttpStatusCode.OK, nameResponse.StatusCode);
            Assert.Equal("Élise", (await ReadProfileAsync(currentBrowser)).GetProperty("firstName").GetString());

            // Le mot de passe : la session en cours continue, l'autre appareil est déconnecté.
            var passwordResponse = await SendSettingsRequestAsync(
                currentBrowser,
                "/api/account/password",
                new { currentPassword = ValidPassword, newPassword = ChangedPassword },
                csrfToken);
            Assert.Equal(HttpStatusCode.OK, passwordResponse.StatusCode);
            await ReadProfileAsync(currentBrowser);
            Assert.Equal(HttpStatusCode.Unauthorized, (await otherBrowser.GetAsync("/api/account/profile")).StatusCode);

            // L'adresse : la demande, le lien parti en arrière-plan, puis la confirmation depuis un autre navigateur, sans session.
            var emailResponse = await SendSettingsRequestAsync(currentBrowser, "/api/account/email", new { password = ChangedPassword, newEmail = newEmail }, csrfToken);
            Assert.Equal(HttpStatusCode.OK, emailResponse.StatusCode);
            await WaitUntilAsync(() => api.EmailSender.CountSentEmailsTo(newEmail) == 1);
            var linkEmail = api.EmailSender.SentEmails.Single(sentEmail => sentEmail.RecipientAddress == newEmail);
            var confirmationResponse = await api.CreateClient().PostAsJsonAsync(
                "/api/account/confirm-email-change",
                new { token = ReadConfirmationToken(linkEmail.Body), password = ChangedPassword });
            Assert.Equal(HttpStatusCode.OK, confirmationResponse.StatusCode);
            await WaitUntilAsync(() => api.EmailSender.CountSentEmailsTo(oldEmail) == 2);

            Assert.Equal(newEmail, (await ReadProfileAsync(currentBrowser)).GetProperty("email").GetString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendLoginRequestAsync(api.CreateClient(), oldEmail, ChangedPassword)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await SendLoginRequestAsync(api.CreateClient(), newEmail, ChangedPassword)).StatusCode);

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var account = await databaseContext.Users.SingleAsync(row => row.Email == newEmail);
            Assert.Equal("Dupont", account.LastName);
            Assert.Equal(1, await databaseContext.SecurityEvents.CountAsync(row =>
                row.UserId == account.Id && row.Type == SecurityEventType.PasswordChange && row.Result == SecurityEventResult.Success));
            Assert.Equal(1, await databaseContext.SecurityEvents.CountAsync(row =>
                row.UserId == account.Id && row.Type == SecurityEventType.EmailChange && row.Result == SecurityEventResult.Success));
            Assert.Equal(1, await databaseContext.EmailTokens.CountAsync(row =>
                row.UserId == account.Id && row.Type == EmailTokenType.EmailChange && row.UsedAt != null));
        }
        finally
        {
            await DeleteAccountAsync(oldEmail);
            await DeleteAccountAsync(newEmail);
        }
    }
}
