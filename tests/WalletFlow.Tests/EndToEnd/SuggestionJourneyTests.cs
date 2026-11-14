using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;
using static WalletFlow.Tests.EndToEnd.RealAccountSteps;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Parcourt les suggestions comme deux utilisateurs, sur la vraie API et la vraie base de test (GR Sacs, SS §4, §5 et §11) :
// le calcul en SQL, le contrôle de la ligne de l'utilisateur, les index uniques et le journal sont les vrais.
// L'horloge est celle de la machine : le mois évalué est le dernier mois complet du jour où le test tourne.
[Collection(RealDatabaseCollection.Name)]
public class SuggestionJourneyTests
{
    private const string SuggestionsEndpoint = "/api/suggestions";

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

    // Lit la liste des suggestions avec ce client.
    private static async Task<JsonElement> ListAsync(HttpClient client)
    {
        var response = await client.GetAsync(SuggestionsEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Donne l'identifiant de la suggestion de cette règle dans une liste lue.
    private static Guid IdOf(JsonElement list, string rule)
    {
        return list.GetProperty("suggestions").EnumerateArray()
            .Single(suggestion => suggestion.GetProperty("rule").GetString() == rule)
            .GetProperty("id").GetGuid();
    }

    // Enregistre pour de vrai un compte bancaire de 1 000 € pour cet utilisateur, et dans le mois évalué 2 000 € de revenus
    // dès son premier jour et 1 700 € de loyer : 1 000 € hors des sacs, et 100 € qui manquent pour épargner 20 %.
    private static async Task SaveBankDataAsync(Guid userId, DateOnly month)
    {
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
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
            BalanceInCents = 100_000,
            BalanceUpdatedAt = DateTime.UtcNow,
            BankConnectionId = connection.Id
        };
        var income = await databaseContext.Categories.AsNoTracking().SingleAsync(row => row.UserId == userId && row.Name == "Revenus");
        var housing = await databaseContext.Categories.AsNoTracking().SingleAsync(row => row.UserId == userId && row.Name == "Logement");
        databaseContext.AddRange(connection, bankAccount);
        databaseContext.BankTransactions.AddRange(
            BankTestRows.CreateTransaction(bankAccount.Id, income.Id, "salaire-" + Guid.NewGuid(), "SALAIRE", 200_000, month),
            BankTestRows.CreateTransaction(bankAccount.Id, housing.Id, "loyer-" + Guid.NewGuid(), "LOYER", -170_000, month.AddDays(4)));
        await databaseContext.SaveChangesAsync();
    }

    // A voit ses deux suggestions, en accepte une dans son sac, ignore l'autre, et voit ses économies. B ne voit rien de A,
    // ne peut répondre à aucune de ses suggestions, ni placer de l'argent dans son sac : 404, noté au journal au nom de B.
    [Fact]
    public async Task SuggestionsLiveTheirWholeLifeThroughTheRealApi()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var emailA = CreateUniqueEmail("parcours-suggestions-a");
        var emailB = CreateUniqueEmail("parcours-suggestions-b");
        var browserA = CreateBrowser(api);
        var browserB = CreateBrowser(api);
        var month = SuggestionRules.FindEvaluatedMonth(UsersCalendar.FindToday(DateTimeOffset.UtcNow));

        try
        {
            await RegisterThenConfirmAsync(api, browserA, emailA);
            await RegisterThenConfirmAsync(api, browserB, emailB);
            var csrfTokenA = await LogInThenGetCsrfTokenAsync(browserA, emailA);
            var csrfTokenB = await LogInThenGetCsrfTokenAsync(browserB, emailB);
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var accountA = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Email == emailA);
            var accountB = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Email == emailB);
            await SaveBankDataAsync(accountA.Id, month);
            var bagOfA = JsonDocument.Parse(await (await SendAsync(browserA, HttpMethod.Post, "/api/bags", new { name = "Épargne" }, csrfTokenA))
                .Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

            var listOfA = await ListAsync(browserA);
            var listOfB = await ListAsync(browserB);

            Assert.Equal(month.ToString("yyyy-MM-dd"), listOfA.GetProperty("month").GetString());
            Assert.Equal(2, listOfA.GetProperty("suggestions").GetArrayLength());
            Assert.Equal(0, listOfB.GetProperty("suggestions").GetArrayLength());
            var unallocated = IdOf(listOfA, "unallocatedMoney");
            var insufficient = IdOf(listOfA, "insufficientSavings");

            var acceptByB = await SendAsync(browserB, HttpMethod.Post, SuggestionsEndpoint + "/" + unallocated + "/accept", new { bagId = bagOfA, amountInCents = 1_000 }, csrfTokenB);
            var ignoreByB = await SendAsync(browserB, HttpMethod.Post, SuggestionsEndpoint + "/" + insufficient + "/ignore", null, csrfTokenB);
            Assert.Equal(HttpStatusCode.NotFound, acceptByB.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, ignoreByB.StatusCode);

            var acceptByA = await SendAsync(browserA, HttpMethod.Post, SuggestionsEndpoint + "/" + unallocated + "/accept", new { bagId = bagOfA, amountInCents = 40_000 }, csrfTokenA);
            var ignoreByA = await SendAsync(browserA, HttpMethod.Post, SuggestionsEndpoint + "/" + insufficient + "/ignore", null, csrfTokenA);
            Assert.Equal(HttpStatusCode.Created, acceptByA.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, ignoreByA.StatusCode);

            var after = await ListAsync(browserA);
            Assert.Equal(0, after.GetProperty("suggestions").GetArrayLength());
            Assert.Equal(40_000, after.GetProperty("savedInCents").GetInt64());
            var saved = await databaseContext.SavingsSuggestions.AsNoTracking().Where(row => row.UserId == accountA.Id).ToListAsync();
            Assert.Equal(2, saved.Count);
            Assert.Equal(SuggestionStatus.Accepted, saved.Single(row => row.Id == unallocated).Status);
            Assert.Equal(SuggestionStatus.Ignored, saved.Single(row => row.Id == insufficient).Status);
            Assert.Equal(month, (await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == accountA.Id)).LastSuggestionMonth);

            // SS §11 : deux essais de B sur les suggestions de A, notés au nom de B. Le sac de A n'est pas noté une seconde fois :
            // la suggestion, introuvable, arrête la requête avant le sac.
            var deniedAccesses = await databaseContext.SecurityEvents.AsNoTracking()
                .Where(row => row.Type == SecurityEventType.AccessDenied)
                .Where(row => row.UserId == accountA.Id || row.UserId == accountB.Id)
                .ToListAsync();
            Assert.Equal(2, deniedAccesses.Count);
            Assert.All(deniedAccesses, row => Assert.Equal(accountB.Id, row.UserId));
        }
        finally
        {
            await DeleteAccountAsync(emailA);
            await DeleteAccountAsync(emailB);
        }
    }
}
