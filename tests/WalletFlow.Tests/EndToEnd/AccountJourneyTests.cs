using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt l'API comme un visiteur, sur la vraie base de test. Les tests des routes remplacent la base,
// et ceux de la base construisent les services à la main : seul ce parcours fait passer chaque requête par le vrai branchement de l'API.
[Collection(RealDatabaseCollection.Name)]
public class AccountJourneyTests
{
    [Fact]
    public async Task VisitorRegistersConfirmsLogsInAndLogsOutThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var client = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        var email = CreateUniqueEmail("parcours");

        try
        {
            var registrationResponse = await client.PostAsJsonAsync(
                RegistrationEndpoint,
                new { email = email, password = ValidPassword });
            Assert.Equal(HttpStatusCode.OK, registrationResponse.StatusCode);

            var confirmationEmail = api.EmailSender.SentEmails.Single(sentEmail => sentEmail.RecipientAddress == email);
            var confirmationResponse = await client.PostAsJsonAsync(
                ConfirmationEndpoint,
                new { token = ReadConfirmationToken(confirmationEmail.Body), password = ValidPassword });
            Assert.Equal(HttpStatusCode.OK, confirmationResponse.StatusCode);

            var loginResponse = await SendLoginRequestAsync(client, email, ValidPassword);
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

            var logoutResponse = await SendLogoutRequestAsync(client, await GetCsrfTokenAsync(client));
            Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

            // La session est détruite côté serveur : le même navigateur n'est plus reconnu.
            var responseAfterLogout = await client.GetAsync(CsrfTokenEndpoint);
            Assert.Equal(HttpStatusCode.Unauthorized, responseAfterLogout.StatusCode);

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var account = await databaseContext.Users.SingleAsync(row => row.Email == email);
            Assert.True(account.IsEmailConfirmed);
            Assert.Equal(8, await databaseContext.Categories.CountAsync(row => row.UserId == account.Id));
            Assert.Equal(1, await databaseContext.Devices.CountAsync(row => row.UserId == account.Id));
            Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == account.Id));
            Assert.Equal(1, await databaseContext.SecurityEvents.CountAsync(row =>
                row.UserId == account.Id
                && row.Type == SecurityEventType.Login
                && row.Result == SecurityEventResult.Success));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
