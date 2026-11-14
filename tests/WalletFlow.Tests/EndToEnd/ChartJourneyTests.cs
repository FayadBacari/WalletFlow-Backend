using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;
using static WalletFlow.Tests.EndToEnd.RealAccountSteps;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt les graphiques comme un utilisateur, sur la vraie base de test (GR Dépenses) : seul le prestataire est faux.
// Les sommes sont faites par la vraie requête SQL, et le mois en cours suit la vraie horloge, à l'heure de Paris.
[Collection(RealDatabaseCollection.Name)]
public class ChartJourneyTests
{
    // Envoie cette requête avec le jeton anti-CSRF.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string address, object body, string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(body) };
        request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);

        return await client.SendAsync(request);
    }

    // Lit le corps JSON d'une réponse réussie.
    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task UserSeesTheChartsOfTheirOperationsThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("parcours-graphiques");
        var browser = CreateBrowser(api);
        var today = UsersCalendar.FindToday(DateTimeOffset.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        try
        {
            await RegisterThenConfirmAsync(api, browser, email);
            var csrfToken = await LogInThenGetCsrfTokenAsync(browser, email);

            // Quatre mois d'historique : la moyenne existe. Ce mois-ci, un remboursement réduit les courses.
            api.BankProvider.AddTransaction("stable-1", monthStart.AddMonths(-4), -1000, "CB CARREFOUR ANCIEN");
            api.BankProvider.AddTransaction("stable-1", monthStart.AddMonths(-2), -6000, "CB CARREFOUR");
            api.BankProvider.AddTransaction("stable-1", monthStart, -3000, "CB CARREFOUR MARKET");
            api.BankProvider.AddTransaction("stable-1", monthStart, 1000, "REMBOURSEMENT CARREFOUR");
            api.BankProvider.AddTransaction("stable-1", monthStart, 250_000, "VIR SEPA SALAIRE");
            api.BankProvider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 152_034));
            await ReadSuccessAsync(await SendAsync(browser, "/api/bank-connections", new { bankName = FakeBankDataProvider.BankName }, csrfToken));
            var state = api.BankProvider.AuthorizationRequests.Last().State;
            await ReadSuccessAsync(await SendAsync(browser, "/api/bank-connections/finish", new { code = "code-1", state = state }, csrfToken));
            var currentMonth = "?from=" + monthStart.ToString("yyyy-MM-dd") + "&to=" + today.ToString("yyyy-MM-dd");

            // La répartition du mois en cours : les courses moins leur remboursement. Le salaire est un revenu.
            var categories = await ReadSuccessAsync(await browser.GetAsync("/api/charts/categories" + currentMonth));
            var food = categories.GetProperty("categories").EnumerateArray().Single();
            Assert.Equal("Alimentation", food.GetProperty("categoryName").GetString());
            Assert.Equal(2000, food.GetProperty("expenseInCents").GetInt64());

            // Les trois derniers mois, celui du milieu sans opération.
            var monthsQuery = "?from=" + monthStart.AddMonths(-2).ToString("yyyy-MM-dd") + "&to=" + today.ToString("yyyy-MM-dd");
            var months = (await ReadSuccessAsync(await browser.GetAsync("/api/charts/months" + monthsQuery))).GetProperty("months").EnumerateArray().ToList();
            Assert.Equal(new List<long> { 6000, 0, 2000 }, months.Select(month => month.GetProperty("expensesInCents").GetInt64()).ToList());
            Assert.Equal(248_000, months[2].GetProperty("savingsInCents").GetInt64());

            // La comparaison : la moyenne des 3 mois complets d'avant ne compte pas le quatrième mois en arrière.
            var comparison = await ReadSuccessAsync(await browser.GetAsync("/api/charts/comparison"));
            Assert.True(comparison.GetProperty("hasEnoughHistory").GetBoolean());
            var foodComparison = comparison.GetProperty("categories").EnumerateArray()
                .Single(category => category.GetProperty("categoryName").GetString() == "Alimentation");
            Assert.Equal(-2000, foodComparison.GetProperty("currentMonthNetInCents").GetInt64());
            Assert.Equal(-2000, foodComparison.GetProperty("threeMonthAverageNetInCents").GetInt64());
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
