using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Api.Configuration;
using WalletFlow.Tests.Fakes;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;
using static WalletFlow.Tests.EndToEnd.RealAccountSteps;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt les opérations comme un utilisateur, sur la vraie base de test (GR Comptes et GR Dépenses) : seul le prestataire est faux.
// Les droits de la base, les clés étrangères, les contraintes, les versions de ligne et la vraie recherche SQL sont les vrais.
[Collection(RealDatabaseCollection.Name)]
public class TransactionJourneyTests
{
    private const string ConnectionsEndpoint = "/api/bank-connections";
    private const string TransactionsEndpoint = "/api/transactions";

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

    // Cherche les opérations de ce navigateur, et renvoie les éléments trouvés.
    private static async Task<List<JsonElement>> SearchAsync(HttpClient browser, string query = "")
    {
        return (await ReadSuccessAsync(await browser.GetAsync(TransactionsEndpoint + query))).GetProperty("items").EnumerateArray().ToList();
    }

    [Fact]
    public async Task UserSeesSortsSearchesAndMarksOperationsThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("parcours-operations");
        var browser = CreateBrowser(api);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            await RegisterThenConfirmAsync(api, browser, email);
            var csrfToken = await LogInThenGetCsrfTokenAsync(browser, email);

            // La connexion : les opérations d'un an arrivent, chacune dans sa catégorie, sans celle qui attend encore.
            api.BankProvider.AddTransaction("stable-1", today.AddDays(-40), -4520, "CB CARREFOUR 03/10");
            api.BankProvider.AddTransaction("stable-1", today.AddDays(-20), 250_000, "VIR SEPA SALAIRE");
            api.BankProvider.AddTransaction("stable-1", today.AddDays(-10), -50_000, "VIR VERS LIVRET");
            api.BankProvider.AddProvidedTransaction("stable-1", new WalletFlow.Application.BankConnections.ProvidedTransaction(
                null, false, today, -999, "EUR", "CB EN ATTENTE"));
            api.BankProvider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 152_034));
            await ReadSuccessAsync(await SendAsync(browser, HttpMethod.Post, ConnectionsEndpoint, new { bankName = FakeBankDataProvider.BankName }, csrfToken));
            var state = api.BankProvider.AuthorizationRequests.Last().State;
            var connected = await ReadSuccessAsync(await SendAsync(browser, HttpMethod.Post, ConnectionsEndpoint + "/finish", new { code = "code-1", state = state }, csrfToken));
            var connectionId = connected.GetProperty("connectionId").GetGuid();

            var operations = await SearchAsync(browser);
            Assert.Equal(new List<string?> { "VIR VERS LIVRET", "VIR SEPA SALAIRE", "CB CARREFOUR 03/10" },
                operations.Select(operation => operation.GetProperty("label").GetString()).ToList());
            Assert.Equal("Revenus", operations[1].GetProperty("categoryName").GetString());
            Assert.Equal("Alimentation", operations[2].GetProperty("categoryName").GetString());

            // Le changement de catégorie, appliqué aux prochaines opérations du même libellé.
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var user = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Email == email);
            var leisure = await databaseContext.Categories.AsNoTracking().SingleAsync(row => row.UserId == user.Id && row.Name == "Loisirs");
            var carrefourId = operations[2].GetProperty("id").GetGuid();
            var changed = await ReadSuccessAsync(await SendAsync(browser, HttpMethod.Put, TransactionsEndpoint + "/" + carrefourId + "/category",
                new { categoryId = leisure.Id, applyToFutureTransactions = true }, csrfToken));
            Assert.Equal("Loisirs", changed.GetProperty("categoryName").GetString());

            // La mise à jour à l'ouverture de l'application : la nouvelle opération suit la règle, l'ancienne n'est pas reçue deux fois.
            api.BankProvider.AddTransaction("stable-1", today, -3900, "cb carrefour 12/11");
            await ReadSuccessAsync(await SendAsync(browser, HttpMethod.Post, ConnectionsEndpoint + "/refresh", null, csrfToken));
            var carrefours = await SearchAsync(browser, "?text=CARREFOUR");
            Assert.Equal(2, carrefours.Count);
            Assert.All(carrefours, operation => Assert.Equal("Loisirs", operation.GetProperty("categoryName").GetString()));
            Assert.Equal(4, (await SearchAsync(browser)).Count);

            // La marque de transfert, et la recherche par montant sans son signe.
            var transferId = (await SearchAsync(browser, "?amountInCents=50000")).Single().GetProperty("id").GetGuid();
            var marked = await ReadSuccessAsync(await SendAsync(browser, HttpMethod.Put, TransactionsEndpoint + "/" + transferId + "/transfer",
                new { isInternalTransfer = true }, csrfToken));
            Assert.True(marked.GetProperty("isInternalTransfer").GetBoolean());

            // Le solde total, puis la déconnexion, qui supprime les opérations.
            var total = await ReadSuccessAsync(await browser.GetAsync("/api/balance"));
            Assert.Equal(152_034, total.GetProperty("totalInCents").GetInt64());
            await ReadSuccessAsync(await SendAsync(browser, HttpMethod.Delete, ConnectionsEndpoint + "/" + connectionId, null, csrfToken));
            Assert.Empty(await SearchAsync(browser));
            Assert.Equal(1, await databaseContext.CategorizationRules.CountAsync(rule => rule.CategoryId == leisure.Id));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
