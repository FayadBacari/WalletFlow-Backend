using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;
using static WalletFlow.Tests.EndToEnd.RealAccountSteps;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt les sacs comme un utilisateur, sur la vraie base de test (GR Sacs, SS §4, §5 et §6) : les sommes en SQL,
// le contrôle de la ligne de l'utilisateur, le déclencheur de suppression et ON DELETE SET NULL sont les vrais.
[Collection(RealDatabaseCollection.Name)]
public class BagJourneyTests
{
    private const string BagsEndpoint = "/api/bags";
    private const string MovementsEndpoint = "/api/bags/movements";

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

    // Lit le corps JSON d'une réponse.
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Lit la liste des sacs avec ce client.
    private static async Task<JsonElement> ListAsync(HttpClient client)
    {
        var response = await client.GetAsync(BagsEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadJsonAsync(response);
    }

    // Déplace ce montant, et renvoie la réponse.
    private static async Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid? sourceBagId, Guid? destinationBagId, int amountInCents, string csrfToken)
    {
        return await SendAsync(client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId, destinationBagId, amountInCents }, csrfToken);
    }

    // Donne le solde de ce sac dans une liste lue.
    private static long BalanceIn(JsonElement list, Guid bagId)
    {
        return list.GetProperty("bags").EnumerateArray()
            .Single(bag => bag.GetProperty("id").GetGuid() == bagId)
            .GetProperty("balanceInCents").GetInt64();
    }

    // Enregistre pour de vrai une connexion bancaire et un compte avec ce solde pour cet utilisateur, et renvoie le compte.
    private static async Task<BankAccount> SaveBankAccountAsync(Guid userId, int balanceInCents)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = "connexion-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = DateTime.UtcNow.AddDays(180),
            UserId = userId
        };
        var bankAccount = new BankAccount
        {
            ExternalAccountId = "compte-" + Guid.NewGuid(),
            Name = "Compte courant",
            BalanceInCents = balanceInCents,
            BalanceUpdatedAt = DateTime.UtcNow,
            BankConnectionId = connection.Id
        };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.AddRange(connection, bankAccount);
        await databaseContext.SaveChangesAsync();

        return bankAccount;
    }

    // SS §4 : sur la vraie API et la vraie base, un utilisateur B ne peut ni lire ni modifier les sacs d'un utilisateur A,
    // ni y prendre ou y mettre de l'argent. Chaque route répond 404, et rien ne change chez A.
    [Fact]
    public async Task AnotherUserCannotTouchTheBagsThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var emailA = CreateUniqueEmail("parcours-sacs-a");
        var emailB = CreateUniqueEmail("parcours-sacs-b");
        var browserA = CreateBrowser(api);
        var browserB = CreateBrowser(api);

        try
        {
            await RegisterThenConfirmAsync(api, browserA, emailA);
            await RegisterThenConfirmAsync(api, browserB, emailB);
            var csrfTokenA = await LogInThenGetCsrfTokenAsync(browserA, emailA);
            var csrfTokenB = await LogInThenGetCsrfTokenAsync(browserB, emailB);
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var accountA = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Email == emailA);
            var accountB = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Email == emailB);
            await SaveBankAccountAsync(accountA.Id, 100_000);
            await SaveBankAccountAsync(accountB.Id, 100_000);
            var bagOfA = (await ReadJsonAsync(await SendAsync(browserA, HttpMethod.Post, BagsEndpoint, new { name = "Vacances" }, csrfTokenA))).GetProperty("id").GetGuid();
            var bagOfB = (await ReadJsonAsync(await SendAsync(browserB, HttpMethod.Post, BagsEndpoint, new { name = "Épargne" }, csrfTokenB))).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(browserA, null, bagOfA, 40_000, csrfTokenA)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(browserB, null, bagOfB, 10_000, csrfTokenB)).StatusCode);

            var listOfB = await ListAsync(browserB);
            var rename = await SendAsync(browserB, HttpMethod.Put, BagsEndpoint + "/" + bagOfA + "/name", new { name = "Piraté" }, csrfTokenB);
            var goal = await SendAsync(browserB, HttpMethod.Put, BagsEndpoint + "/" + bagOfA + "/goal", new { targetAmountInCents = 1, targetDate = (DateOnly?)null }, csrfTokenB);
            var takeFromA = await MoveAsync(browserB, bagOfA, bagOfB, 40_000, csrfTokenB);
            var giveToA = await MoveAsync(browserB, bagOfB, bagOfA, 10_000, csrfTokenB);
            var withdrawFromA = await MoveAsync(browserB, bagOfA, null, 40_000, csrfTokenB);
            var history = await browserB.GetAsync(BagsEndpoint + "/" + bagOfA + "/movements");
            var deletion = await SendAsync(browserB, HttpMethod.Delete, BagsEndpoint + "/" + bagOfA, null, csrfTokenB);

            Assert.DoesNotContain(listOfB.GetProperty("bags").EnumerateArray(), bag => bag.GetProperty("id").GetGuid() == bagOfA);
            Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, goal.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, takeFromA.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, giveToA.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, withdrawFromA.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, deletion.StatusCode);
            var listOfA = await ListAsync(browserA);
            var bagOfAView = listOfA.GetProperty("bags").EnumerateArray().Single();
            Assert.Equal("Vacances", bagOfAView.GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.Null, bagOfAView.GetProperty("targetAmountInCents").ValueKind);
            Assert.Equal(40_000, BalanceIn(listOfA, bagOfA));
            Assert.Equal(10_000, BalanceIn(await ListAsync(browserB), bagOfB));

            // SS §11 : chaque tentative de B sur le sac de A est notée au journal, au nom de B, une par sac de A visé.
            var deniedAccesses = await databaseContext.SecurityEvents.AsNoTracking()
                .Where(row => row.Type == SecurityEventType.AccessDenied)
                .Where(row => row.UserId == accountA.Id || row.UserId == accountB.Id)
                .ToListAsync();
            Assert.Equal(7, deniedAccesses.Count);
            Assert.All(deniedAccesses, row => Assert.Equal(accountB.Id, row.UserId));
            Assert.All(deniedAccesses, row => Assert.Equal(SecurityEventResult.Failure, row.Result));
        }
        finally
        {
            await DeleteAccountAsync(emailA);
            await DeleteAccountAsync(emailB);
        }
    }

    [Fact]
    public async Task UserFillsMovesThenDeletesBagsThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("parcours-sacs");
        var browser = CreateBrowser(api);

        try
        {
            await RegisterThenConfirmAsync(api, browser, email);
            var csrfToken = await LogInThenGetCsrfTokenAsync(browser, email);
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var account = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Email == email);
            var bankAccount = await SaveBankAccountAsync(account.Id, 100_000);
            var goalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200);

            var holidaysCreation = await SendAsync(browser, HttpMethod.Post, BagsEndpoint, new { name = "Vacances", targetAmountInCents = 60_000, targetDate = goalDate }, csrfToken);
            var savingsCreation = await SendAsync(browser, HttpMethod.Post, BagsEndpoint, new { name = "Épargne" }, csrfToken);
            Assert.Equal(HttpStatusCode.Created, holidaysCreation.StatusCode);
            Assert.Equal(HttpStatusCode.Created, savingsCreation.StatusCode);
            var holidaysId = (await ReadJsonAsync(holidaysCreation)).GetProperty("id").GetGuid();
            var savingsId = (await ReadJsonAsync(savingsCreation)).GetProperty("id").GetGuid();

            // Les trois sortes de déplacement, puis un ajout qui dépasserait le solde total.
            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(browser, null, holidaysId, 50_000, csrfToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(browser, holidaysId, savingsId, 20_000, csrfToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(browser, savingsId, null, 5000, csrfToken)).StatusCode);
            var tooMuch = await MoveAsync(browser, null, savingsId, 65_001, csrfToken);
            Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
            Assert.Equal(ErrorMessages.AvailableMoneyTooLow, (await ReadJsonAsync(tooMuch)).GetProperty("errors").GetProperty("AmountInCents")[0].GetString());

            var list = await ListAsync(browser);
            Assert.Equal(100_000, list.GetProperty("totalBalanceInCents").GetInt64());
            Assert.Equal(45_000, list.GetProperty("bagsTotalInCents").GetInt64());
            Assert.Equal(55_000, list.GetProperty("availableInCents").GetInt64());
            Assert.Equal(30_000, BalanceIn(list, holidaysId));
            Assert.Equal(15_000, BalanceIn(list, savingsId));
            Assert.Equal(50, list.GetProperty("bags").EnumerateArray().Single(bag => bag.GetProperty("id").GetGuid() == holidaysId).GetProperty("progressPercent").GetInt32());

            var history = await ReadJsonAsync(await browser.GetAsync(BagsEndpoint + "/" + holidaysId + "/movements"));
            var items = history.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
            Assert.Equal(-20_000, items[0].GetProperty("amountInCents").GetInt32());
            Assert.Equal("Épargne", items[0].GetProperty("otherBagName").GetString());
            Assert.Equal(50_000, items[1].GetProperty("amountInCents").GetInt32());
            Assert.Equal(JsonValueKind.Null, items[1].GetProperty("otherBagId").ValueKind);

            var rename = await SendAsync(browser, HttpMethod.Put, BagsEndpoint + "/" + holidaysId + "/name", new { name = "Voyage" }, csrfToken);
            var goalRemoval = await SendAsync(browser, HttpMethod.Put, BagsEndpoint + "/" + holidaysId + "/goal", new { targetAmountInCents = (int?)null, targetDate = (DateOnly?)null }, csrfToken);
            Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
            Assert.Equal(HttpStatusCode.OK, goalRemoval.StatusCode);

            // GR Sacs : le solde de la banque passe sous le total des sacs. L'argent disponible devient négatif : c'est le montant à retirer.
            // Ajouter dans un sac est refusé, mais retirer vers l'argent disponible reste permis.
            await databaseContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE compte_bancaire SET solde_compte = 35000 WHERE id_compte = {bankAccount.Id}");
            Assert.Equal(-10_000, (await ListAsync(browser)).GetProperty("availableInCents").GetInt64());
            Assert.Equal(HttpStatusCode.BadRequest, (await MoveAsync(browser, null, savingsId, 1, csrfToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await MoveAsync(browser, holidaysId, null, 10_000, csrfToken)).StatusCode);
            Assert.Equal(0, (await ListAsync(browser)).GetProperty("availableInCents").GetInt64());

            // GR Sacs : supprimer un sac remet son montant dans l'argent disponible. L'autre sac garde son solde et son historique.
            var deletion = await SendAsync(browser, HttpMethod.Delete, BagsEndpoint + "/" + holidaysId, null, csrfToken);
            Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
            var listAfterDeletion = await ListAsync(browser);
            Assert.Single(listAfterDeletion.GetProperty("bags").EnumerateArray());
            Assert.Equal(15_000, BalanceIn(listAfterDeletion, savingsId));
            Assert.Equal(20_000, listAfterDeletion.GetProperty("availableInCents").GetInt64());
            var savingsHistory = await ReadJsonAsync(await browser.GetAsync(BagsEndpoint + "/" + savingsId + "/movements"));
            Assert.Equal(2, savingsHistory.GetProperty("totalCount").GetInt32());
            Assert.All(savingsHistory.GetProperty("items").EnumerateArray(), item =>
                Assert.Equal(JsonValueKind.Null, item.GetProperty("otherBagId").ValueKind));
            Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(BagsEndpoint + "/" + holidaysId + "/movements")).StatusCode);
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
