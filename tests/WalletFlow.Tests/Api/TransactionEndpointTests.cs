using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes des opérations et du solde total (GR Comptes, GR Dépenses, SS §4, §6 et §10) dans la vraie API,
// avec un faux prestataire et une fausse base.
public class TransactionEndpointTests
{
    private const string TransactionsEndpoint = "/api/transactions";
    private const string BalanceEndpoint = "/api/balance";
    private const string ConnectionsEndpoint = "/api/bank-connections";
    private const string FinishEndpoint = "/api/bank-connections/finish";
    private const string OtherUserEmail = "autre-utilisateur@exemple.test";

    // Un utilisateur connecté : son navigateur, son jeton anti-CSRF et son compte.
    private sealed record LoggedInUser(HttpClient Client, string CsrfToken, User Account);

    // Ajoute un compte avec ses 8 catégories par défaut, puis le connecte dans un nouveau navigateur.
    private static async Task<LoggedInUser> LogInAsync(ApiFactoryWithFakes api, string email = Email)
    {
        var account = await AddAccountAsync(api, email);
        api.CategoryRepository.SavedCategories.AddRange(DefaultCategories.CreateForUser(account.Id));
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client, email);

        return new LoggedInUser(client, csrfToken, account);
    }

    // Envoie cette requête, avec ce corps et ce jeton anti-CSRF si le test en donne.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string address, object? body, string? csrfToken)
    {
        var request = new HttpRequestMessage(method, address);

        if (body != null)
        {
            request.Content = body is string json ? new StringContent(json, System.Text.Encoding.UTF8, "application/json") : JsonContent.Create(body);
        }

        if (csrfToken != null)
        {
            request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);
        }

        return await client.SendAsync(request);
    }

    // Connecte par l'API une banque dont le compte, reconnu par cet identifiant stable, a déjà ces opérations.
    private static async Task ConnectAsync(ApiFactoryWithFakes api, LoggedInUser user, string stableId, params (int DaysAgo, int AmountInCents, string Label)[] operations)
    {
        var today = DateOnly.FromDateTime(api.Clock.GetUtcNow().UtcDateTime);

        foreach (var operation in operations)
        {
            api.BankProvider.AddTransaction(stableId, today.AddDays(-operation.DaysAgo), operation.AmountInCents, operation.Label);
        }

        var start = await SendAsync(user.Client, HttpMethod.Post, ConnectionsEndpoint, new { bankName = FakeBankDataProvider.BankName }, user.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var code = "code-" + Guid.NewGuid().ToString("N");
        api.BankProvider.AddSession(code, new TestBankAccount(stableId, "Compte courant", "EUR", 152_034));

        var state = api.BankProvider.AuthorizationRequests.Last().State;
        var finish = await SendAsync(user.Client, HttpMethod.Post, FinishEndpoint, new { code = code, state = state }, user.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
    }

    // Lit le corps JSON d'une réponse.
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Lit le message d'erreur de ce champ dans une réponse refusée.
    private static async Task<string?> ReadFieldErrorAsync(HttpResponseMessage response, string field)
    {
        return (await ReadJsonAsync(response)).GetProperty("errors").GetProperty(field)[0].GetString();
    }

    // Cherche les opérations de ce navigateur, et renvoie les éléments trouvés.
    private static async Task<List<JsonElement>> SearchAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(TransactionsEndpoint + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
    }

    // Donne l'identifiant de l'opération de ce libellé.
    private static Guid IdOf(List<JsonElement> items, string label)
    {
        return items.Single(item => item.GetProperty("label").GetString() == label).GetProperty("id").GetGuid();
    }

    // Donne la catégorie de cet utilisateur qui porte ce nom.
    private static Category CategoryOf(ApiFactoryWithFakes api, LoggedInUser user, string name)
    {
        return api.CategoryRepository.SavedCategories.Single(category => category.UserId == user.Account.Id && category.Name == name);
    }

    [Fact]
    public async Task EveryTransactionRouteNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        var anyId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(TransactionsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(BalanceEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Put, TransactionsEndpoint + "/" + anyId + "/category", new { categoryId = anyId, applyToFutureTransactions = false }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Put, TransactionsEndpoint + "/" + anyId + "/transfer", new { isInternalTransfer = true }, null)).StatusCode);
    }

    // SS §6 : sans le jeton anti-CSRF, aucune opération ne change.
    [Fact]
    public async Task WriteRoutesWithoutCsrfTokenChangeNothing()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        await ConnectAsync(api, user, "stable-1", (1, -4520, "CB CARREFOUR"));
        var operationId = IdOf(await SearchAsync(user.Client), "CB CARREFOUR");
        var leisure = CategoryOf(api, user, "Loisirs");

        var category = await SendAsync(user.Client, HttpMethod.Put, TransactionsEndpoint + "/" + operationId + "/category",
            new { categoryId = leisure.Id, applyToFutureTransactions = true }, null);
        var transfer = await SendAsync(user.Client, HttpMethod.Put, TransactionsEndpoint + "/" + operationId + "/transfer",
            new { isInternalTransfer = true }, null);

        Assert.Equal(HttpStatusCode.BadRequest, category.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, transfer.StatusCode);
        var operation = api.TransactionRepository.SavedTransactions.Single();
        Assert.Equal(CategoryOf(api, user, "Alimentation").Id, operation.CategoryId);
        Assert.False(operation.IsInternalTransfer);
        Assert.Empty(api.TransactionRepository.SavedRules);
    }

    // GR Comptes : les opérations arrivent à la connexion, les plus récentes d'abord, avec le nom de leur compte et de leur
    // catégorie. Ce sont des données financières : aucun navigateur ni intermédiaire ne les garde.
    [Fact]
    public async Task SearchGivesTheOperationsAsTheUserSeesThem()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        await ConnectAsync(api, user, "stable-1", (2, -4520, "CB CARREFOUR 02/11"), (1, 250_000, "VIR SALAIRE"));

        var response = await user.Client.GetAsync(TransactionsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var page = await ReadJsonAsync(response);
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(50, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        var newest = page.GetProperty("items")[0];
        Assert.Equal("VIR SALAIRE", newest.GetProperty("label").GetString());
        Assert.Equal(250_000, newest.GetProperty("amountInCents").GetInt32());
        Assert.Equal("Revenus", newest.GetProperty("categoryName").GetString());
        Assert.Equal("Compte courant", newest.GetProperty("accountName").GetString());
        Assert.False(newest.GetProperty("isInternalTransfer").GetBoolean());
    }

    // GR Comptes : le libellé, le montant et les dates se cherchent dans l'adresse.
    [Fact]
    public async Task SearchCriteriaComeFromTheAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        await ConnectAsync(api, user, "stable-1", (3, -4520, "CB CARREFOUR"), (2, 4520, "REMBOURSEMENT"), (1, -1000, "CB SNCF"));
        var today = DateOnly.FromDateTime(api.Clock.GetUtcNow().UtcDateTime);

        Assert.Single(await SearchAsync(user.Client, "?text=carrefour"));
        Assert.Equal(2, (await SearchAsync(user.Client, "?amountInCents=4520")).Count);
        Assert.Equal(2, (await SearchAsync(user.Client, "?from=" + today.AddDays(-2).ToString("yyyy-MM-dd") + "&to=" + today.ToString("yyyy-MM-dd"))).Count);
        Assert.Empty(await SearchAsync(user.Client, "?page=2"));
    }

    // SS §10 : une valeur illisible dans l'adresse reçoit un message en français, qui ne recopie jamais ce que le visiteur a écrit.
    [Fact]
    public async Task UnreadableSearchValuesAreRefusedInFrench()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var amount = await user.Client.GetAsync(TransactionsEndpoint + "?amountInCents=abc<script>");
        var date = await user.Client.GetAsync(TransactionsEndpoint + "?from=pas-une-date");

        Assert.Equal(HttpStatusCode.BadRequest, amount.StatusCode);
        Assert.Equal(ApiErrorConfiguration.InvalidValueMessage, await ReadFieldErrorAsync(amount, "AmountInCents"));
        Assert.DoesNotContain("script", await amount.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, date.StatusCode);
        Assert.Equal(ApiErrorConfiguration.InvalidValueMessage, await ReadFieldErrorAsync(date, "From"));
    }

    // Les critères hors limites sont refusés sur leur champ, avec leur message.
    [Fact]
    public async Task SearchCriteriaOutsideTheLimitsAreRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var amount = await user.Client.GetAsync(TransactionsEndpoint + "?amountInCents=0");
        var dates = await user.Client.GetAsync(TransactionsEndpoint + "?from=2026-11-05&to=2026-11-01");
        var page = await user.Client.GetAsync(TransactionsEndpoint + "?page=0");
        var text = await user.Client.GetAsync(TransactionsEndpoint + "?text=" + new string('a', 256));

        Assert.Equal(ErrorMessages.TransactionSearchAmountBreaksRules, await ReadFieldErrorAsync(amount, "AmountInCents"));
        Assert.Equal(ErrorMessages.DatesReversed, await ReadFieldErrorAsync(dates, "To"));
        Assert.Equal(ErrorMessages.TransactionSearchPageBreaksRules, await ReadFieldErrorAsync(page, "Page"));
        Assert.Equal(ErrorMessages.TransactionSearchTextTooLong, await ReadFieldErrorAsync(text, "Text"));
    }

    // GR Dépenses : l'opération change de catégorie, la règle est créée, et la réponse montre l'opération telle qu'elle est.
    [Fact]
    public async Task CategoryChangeAnswersTheChangedOperation()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        await ConnectAsync(api, user, "stable-1", (1, -4520, "CB CARREFOUR 02/11"));
        var operationId = IdOf(await SearchAsync(user.Client), "CB CARREFOUR 02/11");
        var leisure = CategoryOf(api, user, "Loisirs");

        var response = await SendAsync(user.Client, HttpMethod.Put, TransactionsEndpoint + "/" + operationId + "/category",
            new { categoryId = leisure.Id, applyToFutureTransactions = true }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var operation = await ReadJsonAsync(response);
        Assert.Equal(leisure.Id, operation.GetProperty("categoryId").GetGuid());
        Assert.Equal("Loisirs", operation.GetProperty("categoryName").GetString());
        Assert.Equal("CB CARREFOUR", Assert.Single(api.TransactionRepository.SavedRules).Label);
    }

    // Les deux champs doivent être écrits, avec leur type : une requête incomplète ne crée jamais une règle par erreur.
    [Theory]
    [InlineData("""{ "categoryId": "00000000-0000-0000-0000-000000000001" }""")]
    [InlineData("""{ "applyToFutureTransactions": true }""")]
    [InlineData("""{ "categoryId": "00000000-0000-0000-0000-000000000001", "applyToFutureTransactions": "true" }""")]
    [InlineData("""{ "categoryId": "pas-un-identifiant", "applyToFutureTransactions": true }""")]
    public async Task IncompleteCategoryRequestIsRefused(string json)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        await ConnectAsync(api, user, "stable-1", (1, -4520, "CB CARREFOUR"));
        var operationId = IdOf(await SearchAsync(user.Client), "CB CARREFOUR");

        var response = await SendAsync(user.Client, HttpMethod.Put, TransactionsEndpoint + "/" + operationId + "/category", json, user.CsrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.TransactionRepository.SavedRules);
    }

    // SS §4 : l'opération ou la catégorie d'un autre utilisateur répond 404, comme une adresse qui n'existe pas.
    [Fact]
    public async Task OperationOrCategoryOfAnotherUserIsNotFound()
    {
        using var api = new ApiFactoryWithFakes();
        var owner = await LogInAsync(api);
        var stranger = await LogInAsync(api, OtherUserEmail);
        await ConnectAsync(api, owner, "stable-1", (1, -4520, "CB CARREFOUR"));
        await ConnectAsync(api, stranger, "stable-2", (1, -1000, "CB SNCF"));
        var ownerOperationId = IdOf(await SearchAsync(owner.Client), "CB CARREFOUR");
        var strangerLeisure = CategoryOf(api, stranger, "Loisirs");

        var otherOperation = await SendAsync(stranger.Client, HttpMethod.Put, TransactionsEndpoint + "/" + ownerOperationId + "/category",
            new { categoryId = strangerLeisure.Id, applyToFutureTransactions = false }, stranger.CsrfToken);
        var otherCategory = await SendAsync(owner.Client, HttpMethod.Put, TransactionsEndpoint + "/" + ownerOperationId + "/category",
            new { categoryId = strangerLeisure.Id, applyToFutureTransactions = false }, owner.CsrfToken);
        var otherTransfer = await SendAsync(stranger.Client, HttpMethod.Put, TransactionsEndpoint + "/" + ownerOperationId + "/transfer",
            new { isInternalTransfer = true }, stranger.CsrfToken);

        Assert.Equal(HttpStatusCode.NotFound, otherOperation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherCategory.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherTransfer.StatusCode);
        Assert.Single(await SearchAsync(stranger.Client));
        Assert.Equal("CB SNCF", (await SearchAsync(stranger.Client))[0].GetProperty("label").GetString());
    }

    // Un libellé sans lettre ne peut pas avoir de règle : le refus est sur le champ ApplyToFutureTransactions.
    [Fact]
    public async Task RuleForALabelWithoutLetterIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        await ConnectAsync(api, user, "stable-1", (1, -4520, "12345"));
        var operationId = IdOf(await SearchAsync(user.Client), "12345");

        var response = await SendAsync(user.Client, HttpMethod.Put, TransactionsEndpoint + "/" + operationId + "/category",
            new { categoryId = CategoryOf(api, user, "Loisirs").Id, applyToFutureTransactions = true }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.TransactionLabelWithoutLetter, await ReadFieldErrorAsync(response, nameof(TransactionCategoryRequest.ApplyToFutureTransactions)));
    }

    // GR Comptes : l'opération est marquée comme transfert, puis la marque est retirée. Le champ doit être écrit.
    [Fact]
    public async Task TransferMarkAnswersTheMarkedOperation()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        await ConnectAsync(api, user, "stable-1", (1, -50_000, "VIR VERS LIVRET"));
        var address = TransactionsEndpoint + "/" + IdOf(await SearchAsync(user.Client), "VIR VERS LIVRET") + "/transfer";

        var marked = await SendAsync(user.Client, HttpMethod.Put, address, new { isInternalTransfer = true }, user.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        Assert.True((await ReadJsonAsync(marked)).GetProperty("isInternalTransfer").GetBoolean());

        var empty = await SendAsync(user.Client, HttpMethod.Put, address, "{}", user.CsrfToken);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.True(api.TransactionRepository.SavedTransactions.Single().IsInternalTransfer);

        var unmarked = await SendAsync(user.Client, HttpMethod.Put, address, new { isInternalTransfer = false }, user.CsrfToken);
        Assert.False((await ReadJsonAsync(unmarked)).GetProperty("isInternalTransfer").GetBoolean());
    }

    // GR Comptes : le solde total additionne les comptes de l'utilisateur, avec la date du plus ancien solde, et n'est jamais gardé.
    [Fact]
    public async Task BalanceGivesTheTotalOfEveryAccount()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        var stranger = await LogInAsync(api, OtherUserEmail);
        await ConnectAsync(api, user, "stable-1");
        await ConnectAsync(api, user, "stable-2");
        await ConnectAsync(api, stranger, "stable-3");

        var response = await user.Client.GetAsync(BalanceEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var total = await ReadJsonAsync(response);
        Assert.Equal(2 * 152_034L, total.GetProperty("totalInCents").GetInt64());
        Assert.Equal(2, total.GetProperty("accountCount").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, total.GetProperty("oldestBalanceUpdatedAt").ValueKind);
    }

    // Les messages écrivent les limites en toutes lettres : ils doivent rester égaux aux règles.
    [Fact]
    public void MessagesGiveTheRealLimits()
    {
        Assert.Contains(TransactionRules.MaximumLabelLength + " caractères", ErrorMessages.TransactionSearchTextTooLong);
        Assert.Equal(10_000, TransactionSearchService.MaximumPage);
        Assert.Contains("1 à 10 000", ErrorMessages.TransactionSearchPageBreaksRules);
        Assert.Equal(1_000_000_000, TransactionRules.MaximumAmountInCents);
        Assert.Contains("10 000 000 €", ErrorMessages.TransactionSearchAmountBreaksRules);
    }
}
