using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.Emails;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Tests.Api;
using WalletFlow.Tests.Fakes;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;
using static WalletFlow.Tests.EndToEnd.RealAccountSteps;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt la suppression du compte comme un utilisateur, sur la vraie API et la vraie base de test (GR Suppression, SS §6, §7
// et §9) : seuls le prestataire et l'envoi des e-mails sont faux. Les cascades, le déclencheur du journal, les versions de ligne
// et la file d'e-mails sont les vrais.
[Collection(RealDatabaseCollection.Name)]
public class AccountDeletionJourneyTests
{
    private const string DeletionEndpoint = "/api/account/delete";
    private const string ConnectionsEndpoint = "/api/bank-connections";
    private const string VisitorAddress = "198.51.100.23";

    // Lit le corps JSON d'une réponse réussie.
    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Connecte la banque des tests comme la page de retour du site, et renvoie l'identifiant de la connexion.
    private static async Task<Guid> ConnectBankAsync(ApiFactoryWithRealDatabase api, HttpClient browser, string csrfToken)
    {
        await ReadSuccessAsync(await SendSettingsRequestAsync(browser, ConnectionsEndpoint, new { bankName = FakeBankDataProvider.BankName }, csrfToken));
        var state = api.BankProvider.AuthorizationRequests.Last().State;
        api.BankProvider.AddSession("code-suppression", new TestBankAccount("stable-suppression", "Compte courant", "EUR", 10_000));

        var connected = await ReadSuccessAsync(await SendSettingsRequestAsync(browser, ConnectionsEndpoint + "/finish", new { code = "code-suppression", state = state }, csrfToken));

        return connected.GetProperty("connectionId").GetGuid();
    }

    // Attend que la tâche de fond ait envoyé l'e-mail de cet objet à cette adresse, puis le donne.
    private static async Task<EmailMessage> WaitForEmailAsync(ApiFactoryWithRealDatabase api, string email, string subject)
    {
        await RecoveryRequestTestValues.WaitUntilAsync(() => api.EmailSender.FindSentEmailsTo(email, subject).Count == 1);

        return api.EmailSender.FindSentEmailsTo(email, subject)[0];
    }

    [Fact]
    public async Task UserRequestsCancelsThenGetsTheAccountErasedThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("parcours-suppression");
        var browser = CreateBrowser(api);
        var otherDevice = CreateBrowser(api);
        browser.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, VisitorAddress);

        try
        {
            await RegisterThenConfirmAsync(api, browser, email);
            var csrfToken = await LogInThenGetCsrfTokenAsync(browser, email);
            await LogInThenGetCsrfTokenAsync(otherDevice, email);
            var connectionId = await ConnectBankAsync(api, browser, csrfToken);
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var userId = (await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Email == email)).Id;
            var visitorIp = IPAddress.Parse(VisitorAddress);

            // La demande : toutes les sessions sont fermées, la banque est révoquée, et un e-mail donne le jour de l'effacement.
            var requested = await ReadSuccessAsync(await SendSettingsRequestAsync(browser, DeletionEndpoint, new { password = ValidPassword }, csrfToken));
            var requestedAt = (await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == userId)).DeletionRequestedAt!.Value;
            var erasureDay = DateOnly.FromDateTime(UsersCalendar.ConvertToUsersTime(AccountDeletionRules.FindErasureMoment(requestedAt)));
            Assert.Equal(erasureDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), requested.GetProperty("erasureDate").GetString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/categories")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/categories")).StatusCode);
            Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == userId));
            var connection = await databaseContext.BankConnections.AsNoTracking().SingleAsync(row => row.Id == connectionId);
            Assert.Equal(BankConnectionStatus.Expired, connection.Status);
            Assert.Equal(new List<string> { connection.ExternalConnectionId }, api.BankProvider.RevokedSessions);
            Assert.Equal(1, await databaseContext.SecurityEvents.CountAsync(row =>
                row.UserId == userId && row.Type == SecurityEventType.DeletionRequest && row.Result == SecurityEventResult.Success
                && row.IpAddress == visitorIp));
            Assert.Equal(1, await databaseContext.SecurityEvents.CountAsync(row =>
                row.UserId == userId && row.Type == SecurityEventType.BankDisconnection && row.Result == SecurityEventResult.Success));
            var requestEmail = await WaitForEmailAsync(api, email, "La suppression de ton compte WalletFlow est demandée");
            Assert.Contains("effacés définitivement le " + FrenchDates.WriteDay(erasureDay) + ".", requestEmail.Body);

            // La connexion annule la demande, et le dit. La banque reste à renouveler.
            var cancelling = await ReadSuccessAsync(await LoginRequestTestValues.SendLoginRequestAsync(browser, email, ValidPassword));
            Assert.True(cancelling.GetProperty("isDeletionCancelled").GetBoolean());
            Assert.Null((await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == userId)).DeletionRequestedAt);
            Assert.Equal(BankConnectionStatus.Expired, (await databaseContext.BankConnections.AsNoTracking().SingleAsync(row => row.Id == connectionId)).Status);
            await WaitForEmailAsync(api, email, "La suppression de ton compte WalletFlow est annulée");

            // Une nouvelle demande, puis 30 jours passent : le passage de la tâche horaire efface le compte, sans révoquer deux fois.
            csrfToken = await LoginRequestTestValues.GetCsrfTokenAsync(browser);
            await ReadSuccessAsync(await SendSettingsRequestAsync(browser, DeletionEndpoint, new { password = ValidPassword }, csrfToken));
            var eventIds = await databaseContext.SecurityEvents.Where(row => row.UserId == userId).Select(row => row.Id).ToListAsync();
            var secondRequestAt = (await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == userId)).DeletionRequestedAt!.Value;
            await databaseContext.Users
                .Where(row => row.Id == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DeletionRequestedAt, secondRequestAt - AccountDeletionRules.ErasureDelay - TimeSpan.FromHours(1)));

            await new AccountDeletionBackgroundService(
                api.Services.GetRequiredService<IServiceScopeFactory>(),
                api.Services.GetRequiredService<TimeProvider>(),
                NullLogger<AccountDeletionBackgroundService>.Instance).RunPassAsync();

            Assert.False(await databaseContext.Users.AnyAsync(row => row.Id == userId));
            Assert.False(await databaseContext.BankConnections.AnyAsync(row => row.Id == connectionId));
            Assert.Single(api.BankProvider.RevokedSessions);
            var keptEvents = await databaseContext.SecurityEvents.AsNoTracking().Where(row => eventIds.Contains(row.Id)).ToListAsync();
            Assert.Equal(eventIds.Count, keptEvents.Count);
            Assert.Contains(keptEvents, row => row.Type == SecurityEventType.DeletionRequest);
            Assert.All(keptEvents, row => Assert.Equal(((Guid?)null, (IPAddress?)null), (row.UserId, row.IpAddress)));
            var erasureEmail = await WaitForEmailAsync(api, email, "Ton compte WalletFlow est effacé");
            Assert.DoesNotContain("http", erasureEmail.Body);
            await RecoveryRequestTestValues.WaitUntilAsync(() => !databaseContext.EmailRequests.Any(row => row.EmailAddress == email));
        }
        finally
        {
            await DeleteAccountAsync(email);
            await DeleteQueuedEmailsToAsync(email);
        }
    }
}
