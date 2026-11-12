using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes des graphiques (GR Dépenses, SS §4, §6 et §10) dans la vraie API, avec un faux prestataire
// et une fausse base. L'horloge des tests marque le 4 novembre 2026 : le mois en cours est novembre 2026.
public class ChartEndpointTests
{
    private const string CategoriesEndpoint = "/api/charts/categories";
    private const string MonthsEndpoint = "/api/charts/months";
    private const string ComparisonEndpoint = "/api/charts/comparison";
    private const string November = "?from=2026-11-01&to=2026-11-30";

    // Ajoute un compte avec ses 8 catégories par défaut, le connecte, puis connecte par l'API une banque avec ces opérations.
    private static async Task<HttpClient> LogInWithOperationsAsync(ApiFactoryWithFakes api, params (int Day, int AmountInCents, string Label)[] operations)
    {
        var account = await AddAccountAsync(api);
        api.CategoryRepository.SavedCategories.AddRange(DefaultCategories.CreateForUser(account.Id));
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        foreach (var operation in operations)
        {
            api.BankProvider.AddTransaction("stable-1", new DateOnly(2026, 11, operation.Day), operation.AmountInCents, operation.Label);
        }

        var start = new HttpRequestMessage(HttpMethod.Post, "/api/bank-connections") { Content = JsonContent.Create(new { bankName = FakeBankDataProvider.BankName }) };
        start.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(start)).StatusCode);
        api.BankProvider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000));
        var finish = new HttpRequestMessage(HttpMethod.Post, "/api/bank-connections/finish")
        {
            Content = JsonContent.Create(new { code = "code-1", state = api.BankProvider.AuthorizationRequests.Last().State })
        };
        finish.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(finish)).StatusCode);

        return client;
    }

    // Lit le corps JSON d'une réponse réussie, qu'aucun navigateur ni intermédiaire ne doit garder.
    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Lit le message d'erreur de ce champ dans une réponse refusée.
    private static async Task<string?> ReadFieldErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").GetProperty(field)[0].GetString();
    }

    [Fact]
    public async Task EveryChartRouteNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(CategoriesEndpoint + November)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(MonthsEndpoint + November)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ComparisonEndpoint)).StatusCode);
    }

    // GR Dépenses : la répartition des dépenses sur la période, un remboursement réduisant sa catégorie, et leur total.
    [Fact]
    public async Task CategoryChartGivesEachExpenseAndTheirTotal()
    {
        using var api = new ApiFactoryWithFakes();
        var client = await LogInWithOperationsAsync(api, (2, -4520, "CB CARREFOUR"), (3, 1250, "REMBOURSEMENT CARREFOUR"), (3, -1000, "CB SNCF"), (4, 250_000, "VIR SALAIRE"));

        var chart = await ReadSuccessAsync(await client.GetAsync(CategoriesEndpoint + November));

        Assert.Equal("2026-11-01", chart.GetProperty("from").GetString());
        Assert.Equal("2026-11-30", chart.GetProperty("to").GetString());
        Assert.Equal(4270, chart.GetProperty("totalExpensesInCents").GetInt64());
        var categories = chart.GetProperty("categories").EnumerateArray().ToList();
        Assert.Equal(new List<string?> { "Alimentation", "Transport" }, categories.Select(category => category.GetProperty("categoryName").GetString()).ToList());
        Assert.Equal(3270, categories[0].GetProperty("expenseInCents").GetInt64());
    }

    // GR Dépenses : les revenus, les dépenses et l'épargne de chaque mois, un mois sans opération valant zéro.
    [Fact]
    public async Task MonthChartGivesEveryMonthOfThePeriod()
    {
        using var api = new ApiFactoryWithFakes();
        var client = await LogInWithOperationsAsync(api, (2, -4520, "CB CARREFOUR"), (4, 250_000, "VIR SALAIRE"));

        var chart = await ReadSuccessAsync(await client.GetAsync(MonthsEndpoint + "?from=2026-10-01&to=2026-11-30"));

        var months = chart.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(2, months.Count);
        Assert.Equal((2026, 10, 0L), (months[0].GetProperty("year").GetInt32(), months[0].GetProperty("month").GetInt32(), months[0].GetProperty("expensesInCents").GetInt64()));
        Assert.Equal(250_000, months[1].GetProperty("incomeInCents").GetInt64());
        Assert.Equal(4520, months[1].GetProperty("expensesInCents").GetInt64());
        Assert.Equal(245_480, months[1].GetProperty("savingsInCents").GetInt64());
    }

    // GR Dépenses : le mois en cours face à la moyenne, avec chaque catégorie. Sans 3 mois d'historique, la moyenne est absente.
    [Fact]
    public async Task ComparisonGivesTheCurrentMonthOfEveryCategory()
    {
        using var api = new ApiFactoryWithFakes();
        var client = await LogInWithOperationsAsync(api, (2, -4520, "CB CARREFOUR"));

        var comparison = await ReadSuccessAsync(await client.GetAsync(ComparisonEndpoint));

        Assert.Equal((2026, 11, 4, 30), (comparison.GetProperty("year").GetInt32(), comparison.GetProperty("month").GetInt32(),
            comparison.GetProperty("daysElapsed").GetInt32(), comparison.GetProperty("daysInMonth").GetInt32()));
        Assert.False(comparison.GetProperty("hasEnoughHistory").GetBoolean());
        var categories = comparison.GetProperty("categories").EnumerateArray().ToList();
        Assert.Equal(8, categories.Count);
        var food = categories.Single(category => category.GetProperty("categoryName").GetString() == "Alimentation");
        Assert.Equal(-4520, food.GetProperty("currentMonthNetInCents").GetInt64());
        Assert.Equal(JsonValueKind.Null, food.GetProperty("threeMonthAverageNetInCents").ValueKind);
        Assert.Equal(JsonValueKind.Null, food.GetProperty("budgetUsedPercent").ValueKind);
        Assert.True(categories.Last().GetProperty("isOtherCategory").GetBoolean());
    }

    // Une période demande ses deux dates : le refus est sur chaque champ qui manque.
    [Fact]
    public async Task MissingDatesAreRefusedOnTheirField()
    {
        using var api = new ApiFactoryWithFakes();
        var client = await LogInWithOperationsAsync(api);

        var noDates = await client.GetAsync(CategoriesEndpoint);
        var noEnd = await client.GetAsync(MonthsEndpoint + "?from=2026-11-01");

        Assert.Equal(ErrorMessages.ChartPeriodRequired, await ReadFieldErrorAsync(noDates, "From"));
        Assert.Equal(ErrorMessages.ChartPeriodRequired, await ReadFieldErrorAsync(await client.GetAsync(CategoriesEndpoint), "To"));
        Assert.Equal(ErrorMessages.ChartPeriodRequired, await ReadFieldErrorAsync(noEnd, "To"));
        var noEndErrors = JsonDocument.Parse(await (await client.GetAsync(MonthsEndpoint + "?from=2026-11-01")).Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        Assert.False(noEndErrors.TryGetProperty("From", out _));
    }

    // Une période à l'envers ou de plus de 10 ans est refusée sur le champ To ; une date illisible, en français, sans être recopiée.
    [Fact]
    public async Task WrongPeriodsAreRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var client = await LogInWithOperationsAsync(api);

        Assert.Equal(ErrorMessages.DatesReversed, await ReadFieldErrorAsync(await client.GetAsync(CategoriesEndpoint + "?from=2026-11-30&to=2026-11-01"), "To"));
        Assert.Equal(ErrorMessages.ChartPeriodTooLong, await ReadFieldErrorAsync(await client.GetAsync(MonthsEndpoint + "?from=2016-12-31&to=2026-12-31"), "To"));
        var unreadable = await client.GetAsync(MonthsEndpoint + "?from=pas-une-date<b>&to=2026-11-30");
        Assert.Equal(ApiErrorConfiguration.InvalidValueMessage, await ReadFieldErrorAsync(unreadable, "From"));
        Assert.DoesNotContain("<b>", await unreadable.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MonthsEndpoint + "?from=2017-01-01&to=2026-12-31")).StatusCode);
    }

    // SS §4 : le compte d'un autre utilisateur, donné en filtre, ne montre rien.
    [Fact]
    public async Task AccountOfAnotherUserShowsNothing()
    {
        using var api = new ApiFactoryWithFakes();
        var client = await LogInWithOperationsAsync(api, (2, -4520, "CB CARREFOUR"));

        var chart = await ReadSuccessAsync(await client.GetAsync(CategoriesEndpoint + November + "&accountId=" + Guid.NewGuid()));

        Assert.Empty(chart.GetProperty("categories").EnumerateArray());
        Assert.Equal(0, chart.GetProperty("totalExpensesInCents").GetInt64());
    }

    // Le message écrit la durée en années : elle doit rester égale à la règle, en mois.
    [Fact]
    public void MessageGivesTheRealLimit()
    {
        Assert.Contains(ChartRules.MaximumPeriodMonths / 12 + " ans", ErrorMessages.ChartPeriodTooLong);
    }
}
