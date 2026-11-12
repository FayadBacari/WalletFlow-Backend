using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;
using static WalletFlow.Tests.EndToEnd.RealAccountSteps;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt la connexion bancaire comme un utilisateur, sur la vraie base de test (GR Comptes, SS §4, §7 et §11) :
// seul le prestataire est faux. Les droits de la base, les clés étrangères, les versions de ligne et le journal sont les vrais.
[Collection(RealDatabaseCollection.Name)]
public class BankConnectionJourneyTests
{
    private const string ConnectionsEndpoint = "/api/bank-connections";

    // Envoie cette requête avec le jeton anti-CSRF.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string address, object? body, string csrfToken)
    {
        var request = new HttpRequestMessage(method, address);

        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);

        return await client.SendAsync(request);
    }

    // Lit le corps JSON d'une réponse réussie.
    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Commence une autorisation à cette adresse, puis revient de la banque avec ce code, comme la page de retour du site.
    private static async Task<JsonElement> AuthorizeAsync(ApiFactoryWithRealDatabase api, HttpClient browser, string csrfToken, string startAddress, object? startBody, string code)
    {
        await ReadSuccessAsync(await SendAsync(browser, HttpMethod.Post, startAddress, startBody, csrfToken));
        var state = api.BankProvider.AuthorizationRequests.Last().State;

        return await ReadSuccessAsync(await SendAsync(browser, HttpMethod.Post, ConnectionsEndpoint + "/finish", new { code = code, state = state }, csrfToken));
    }

    [Fact]
    public async Task UserConnectsUpdatesRenewsThenDisconnectsABankThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("parcours-banque");
        var browser = CreateBrowser(api);

        try
        {
            await RegisterThenConfirmAsync(api, browser, email);
            var csrfToken = await LogInThenGetCsrfTokenAsync(browser, email);
            await ReadSuccessAsync(await browser.GetAsync("/api/banks"));

            // La connexion : seuls les comptes en euros sont gardés.
            api.BankProvider.AddSession("code-1",
                new TestBankAccount("stable-1", "Compte courant", "EUR", 152_034),
                new TestBankAccount("stable-2", "Compte en dollars", "USD", 100));
            var connected = await AuthorizeAsync(api, browser, csrfToken, ConnectionsEndpoint, new { bankName = FakeBankDataProvider.BankName }, "code-1");
            var connectionId = connected.GetProperty("connectionId").GetGuid();

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var account = await databaseContext.BankAccounts.AsNoTracking().SingleAsync(row => row.BankConnectionId == connectionId);
            var user = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Email == email);
            var leisure = await databaseContext.Categories.AsNoTracking().SingleAsync(row => row.UserId == user.Id && row.Name == "Loisirs");
            databaseContext.BankTransactions.Add(new BankTransaction
            {
                ExternalTransactionId = "operation-1",
                TransactionDate = new DateOnly(2026, 11, 10),
                Label = "CINEMA",
                AmountInCents = -1200,
                BankAccountId = account.Id,
                CategoryId = leisure.Id
            });
            await databaseContext.SaveChangesAsync();
            Assert.Equal(152_034, account.BalanceInCents);

            // La mise à jour à l'ouverture de l'application.
            api.BankProvider.ChangeBalance("stable-1", 99_900);
            var refreshed = await ReadSuccessAsync(await SendAsync(browser, HttpMethod.Post, ConnectionsEndpoint + "/refresh", null, csrfToken));
            Assert.Equal(99_900, refreshed[0].GetProperty("accounts")[0].GetProperty("balanceInCents").GetInt32());

            // Le renouvellement garde le même compte, et sa transaction.
            api.BankProvider.AddSession("code-2", new TestBankAccount("stable-1", "Compte courant", "EUR", 50_000));
            var renewed = await AuthorizeAsync(api, browser, csrfToken, ConnectionsEndpoint + "/" + connectionId + "/renew", null, "code-2");
            Assert.True(renewed.GetProperty("isRenewal").GetBoolean());
            Assert.Equal(account.Id, (await databaseContext.BankAccounts.AsNoTracking().SingleAsync(row => row.BankConnectionId == connectionId)).Id);
            Assert.Equal(1, await databaseContext.BankTransactions.CountAsync(row => row.BankAccountId == account.Id));

            // La déconnexion supprime la banque, son compte et sa transaction.
            await ReadSuccessAsync(await SendAsync(browser, HttpMethod.Delete, ConnectionsEndpoint + "/" + connectionId, null, csrfToken));
            Assert.False(await databaseContext.BankConnections.AnyAsync(row => row.Id == connectionId));
            Assert.False(await databaseContext.BankAccounts.AnyAsync(row => row.Id == account.Id));
            Assert.False(await databaseContext.BankTransactions.AnyAsync(row => row.BankAccountId == account.Id));
            Assert.Equal(2, await databaseContext.SecurityEvents.CountAsync(row =>
                row.UserId == user.Id && row.Type == SecurityEventType.BankConnection && row.Result == SecurityEventResult.Success));
            Assert.Equal(1, await databaseContext.SecurityEvents.CountAsync(row =>
                row.UserId == user.Id && row.Type == SecurityEventType.BankDisconnection && row.Result == SecurityEventResult.Success));
            Assert.Equal(2, api.BankProvider.RevokedSessions.Count);
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
