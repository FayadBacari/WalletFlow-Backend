using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;
using static WalletFlow.Tests.EndToEnd.RealAccountSteps;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt les catégories comme un utilisateur, sur la vraie base de test (GR Dépenses, SS §4 et §6) : les catégories
// créées à l'inscription, l'index unique, la clé étrangère différée et le déplacement des transactions sont les vrais.
[Collection(RealDatabaseCollection.Name)]
public class CategoryJourneyTests
{
    private const string CategoriesEndpoint = "/api/categories";

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

    // Lit la liste des catégories avec ce client.
    private static async Task<List<JsonElement>> ListAsync(HttpClient client)
    {
        var response = await client.GetAsync(CategoriesEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToList();
    }

    // Enregistre pour de vrai une transaction de ce compte, classée dans cette catégorie, avec sa connexion et son compte bancaires.
    private static async Task<BankTransaction> SaveTransactionAsync(Guid userId, Guid categoryId)
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
            BalanceInCents = 100000,
            BalanceUpdatedAt = DateTime.UtcNow,
            BankConnectionId = connection.Id
        };
        var transaction = new BankTransaction
        {
            ExternalTransactionId = "operation-" + Guid.NewGuid(),
            TransactionDate = new DateOnly(2026, 11, 10),
            Label = "NETFLIX",
            AmountInCents = -1399,
            BankAccountId = bankAccount.Id,
            CategoryId = categoryId
        };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.AddRange(connection, bankAccount, transaction);
        await databaseContext.SaveChangesAsync();

        return transaction;
    }

    [Fact]
    public async Task UserCreatesRenamesBudgetsThenDeletesACategoryThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("parcours-categories");
        var browser = CreateBrowser(api);

        try
        {
            await RegisterThenConfirmAsync(api, browser, email);
            var csrfToken = await LogInThenGetCsrfTokenAsync(browser, email);

            // L'inscription a créé les 8 catégories par défaut, « Autres » à la fin.
            var defaultCategories = await ListAsync(browser);
            Assert.Equal(8, defaultCategories.Count);
            Assert.Equal("Autres", defaultCategories.Last().GetProperty("name").GetString());

            var creation = await SendAsync(browser, HttpMethod.Post, CategoriesEndpoint, new { name = "Abonnements" }, csrfToken);
            Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
            var categoryId = JsonDocument.Parse(await creation.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

            // Le même nom en majuscules : la vraie base compare avec lower(), comme son index unique.
            var duplicate = await SendAsync(browser, HttpMethod.Post, CategoriesEndpoint, new { name = "ABONNEMENTS" }, csrfToken);
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

            var rename = await SendAsync(browser, HttpMethod.Put, CategoriesEndpoint + "/" + categoryId + "/name", new { name = "Streaming" }, csrfToken);
            var budget = await SendAsync(browser, HttpMethod.Put, CategoriesEndpoint + "/" + categoryId + "/budget", new { monthlyBudgetInCents = 3000 }, csrfToken);
            Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
            Assert.Equal(HttpStatusCode.OK, budget.StatusCode);
            Assert.Contains(await ListAsync(browser), category =>
                category.GetProperty("name").GetString() == "Streaming"
                && category.GetProperty("monthlyBudgetInCents").GetInt32() == 3000);

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var account = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Email == email);
            var transaction = await SaveTransactionAsync(account.Id, categoryId);

            var deletion = await SendAsync(browser, HttpMethod.Delete, CategoriesEndpoint + "/" + categoryId, null, csrfToken);

            // GR Dépenses : la transaction est maintenant dans « Autres », et la catégorie a disparu.
            Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
            var otherCategory = await databaseContext.Categories.AsNoTracking().SingleAsync(row => row.UserId == account.Id && row.IsOtherCategory);
            var movedTransaction = await databaseContext.BankTransactions.AsNoTracking().SingleAsync(row => row.Id == transaction.Id);
            Assert.Equal(otherCategory.Id, movedTransaction.CategoryId);
            Assert.False(await databaseContext.Categories.AnyAsync(row => row.Id == categoryId));
            Assert.Equal(8, (await ListAsync(browser)).Count);
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
