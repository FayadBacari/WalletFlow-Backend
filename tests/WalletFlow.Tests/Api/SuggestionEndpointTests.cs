using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes des suggestions (GR Sacs, SS §4, §5, §6 et §11) dans la vraie API, avec une fausse base.
// L'horloge des tests marque le 4 novembre 2026 : le mois évalué est octobre.
public class SuggestionEndpointTests
{
    private const string SuggestionsEndpoint = "/api/suggestions";
    private const string OtherUserEmail = "autre-utilisateur@exemple.test";
    private static readonly DateOnly October = new DateOnly(2026, 10, 1);

    // Un utilisateur connecté : son navigateur, son jeton anti-CSRF et son compte.
    private sealed record LoggedInUser(HttpClient Client, string CsrfToken, User Account);

    // Ajoute un compte avec ses catégories par défaut, le connecte dans un nouveau navigateur, et lui donne un compte bancaire
    // avec ce solde.
    private static async Task<LoggedInUser> LogInWithBalanceAsync(ApiFactoryWithFakes api, int balanceInCents, string email = Email)
    {
        var account = await AddAccountAsync(api, email);
        api.CategoryRepository.SavedCategories.AddRange(DefaultCategories.CreateForUser(account.Id));
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client, email);
        var connection = new BankConnection
        {
            ExternalConnectionId = "session-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(100),
            UserId = account.Id
        };
        api.BankRepository.SavedConnections.Add(connection);
        api.BankRepository.SavedAccounts.Add(new BankAccount
        {
            ExternalAccountId = "compte-" + Guid.NewGuid(),
            Name = "Compte courant",
            BalanceInCents = balanceInCents,
            BalanceUpdatedAt = api.Clock.GetUtcNow().UtcDateTime,
            BankConnectionId = connection.Id
        });

        return new LoggedInUser(client, csrfToken, account);
    }

    // Garde directement une suggestion proposée de ce compte pour octobre.
    private static SavingsSuggestion SaveSuggestion(
        ApiFactoryWithFakes api,
        User account,
        SuggestionRule rule,
        int savingsInCents,
        Guid? categoryId = null)
    {
        var suggestion = new SavingsSuggestion
        {
            Rule = rule,
            Month = October,
            MonthlySavingsInCents = savingsInCents,
            UserId = account.Id,
            CategoryId = categoryId
        };
        api.SuggestionRepository.SavedSuggestions.Add(suggestion);

        return suggestion;
    }

    // Garde directement un sac de ce compte.
    private static Bag SaveBag(ApiFactoryWithFakes api, User account, string name)
    {
        var bag = new Bag { Name = name, UserId = account.Id };
        api.BagRepository.SavedBags.Add(bag);

        return bag;
    }

    // Envoie cette requête, avec ce corps et ce jeton anti-CSRF si le test en donne.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string address, object? body, string? csrfToken)
    {
        var request = new HttpRequestMessage(method, address);

        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (csrfToken != null)
        {
            request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);
        }

        return await client.SendAsync(request);
    }

    // Accepte cette suggestion en plaçant ce montant dans ce sac.
    private static async Task<HttpResponseMessage> AcceptAsync(LoggedInUser user, Guid suggestionId, Guid bagId, int amountInCents)
    {
        return await SendAsync(user.Client, HttpMethod.Post, AcceptAddress(suggestionId), new { bagId, amountInCents }, user.CsrfToken);
    }

    // Ignore cette suggestion.
    private static async Task<HttpResponseMessage> IgnoreAsync(LoggedInUser user, Guid suggestionId)
    {
        return await SendAsync(user.Client, HttpMethod.Post, IgnoreAddress(suggestionId), null, user.CsrfToken);
    }

    // Adresse de l'acceptation de cette suggestion.
    private static string AcceptAddress(Guid suggestionId)
    {
        return SuggestionsEndpoint + "/" + suggestionId + "/accept";
    }

    // Adresse de l'ignorance de cette suggestion.
    private static string IgnoreAddress(Guid suggestionId)
    {
        return SuggestionsEndpoint + "/" + suggestionId + "/ignore";
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

    // Compte les accès refusés notés au journal pour ce compte (SS §11).
    private static int CountAccessDenied(ApiFactoryWithFakes api, User account)
    {
        return api.SecurityEventRepository.SavedSecurityEvents.Count(securityEvent =>
            securityEvent.Type == SecurityEventType.AccessDenied
            && securityEvent.Result == SecurityEventResult.Failure
            && securityEvent.UserId == account.Id);
    }

    // --- Session et jeton anti-CSRF

    [Fact]
    public async Task EverySuggestionRouteNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateBrowserClient(api);
        var anyId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(SuggestionsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, AcceptAddress(anyId), new { bagId = anyId, amountInCents = 1 }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, IgnoreAddress(anyId), null, null)).StatusCode);
    }

    // SS §6 : sans jeton anti-CSRF, une réponse à une suggestion est refusée, et rien ne change.
    [Fact]
    public async Task AnswersWithoutCsrfTokenChangeNothing()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var bag = SaveBag(api, user.Account, "Épargne");
        var suggestion = SaveSuggestion(api, user.Account, SuggestionRule.UnallocatedMoney, 30_000);

        var acceptance = await SendAsync(user.Client, HttpMethod.Post, AcceptAddress(suggestion.Id), new { bagId = bag.Id, amountInCents = 30_000 }, null);
        var dismissal = await SendAsync(user.Client, HttpMethod.Post, IgnoreAddress(suggestion.Id), null, null);

        Assert.Equal(HttpStatusCode.BadRequest, acceptance.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, dismissal.StatusCode);
        Assert.Equal(SuggestionStatus.Proposed, suggestion.Status);
        Assert.Empty(api.BagRepository.SavedMovements);
    }

    // --- Liste

    // La première lecture calcule octobre : 1 000 € hors des sacs, et 2 000 € de revenus pour 1 700 € de dépenses.
    // La réponse donne le mois, la règle en clair, le montant par mois, et les économies déjà réalisées.
    // Ce sont des données financières : la réponse interdit toute copie en cache.
    [Fact]
    public async Task FirstReadCalculatesTheMonth()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var housing = api.CategoryRepository.SavedCategories.Single(category => category.UserId == user.Account.Id && category.Name == "Logement");
        var income = api.CategoryRepository.SavedCategories.Single(category => category.UserId == user.Account.Id && category.Name == "Revenus");
        var bankAccount = api.BankRepository.SavedAccounts.Single();
        api.TransactionRepository.SavedTransactions.Add(new BankTransaction
        {
            ExternalTransactionId = "salaire", TransactionDate = October, Label = "SALAIRE", AmountInCents = 200_000,
            BankAccountId = bankAccount.Id, CategoryId = income.Id
        });
        api.TransactionRepository.SavedTransactions.Add(new BankTransaction
        {
            ExternalTransactionId = "loyer", TransactionDate = October.AddDays(4), Label = "LOYER", AmountInCents = -170_000,
            BankAccountId = bankAccount.Id, CategoryId = housing.Id
        });

        var response = await user.Client.GetAsync(SuggestionsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await ReadJsonAsync(response);
        Assert.Equal("2026-10-01", body.GetProperty("month").GetString());
        Assert.Equal(0, body.GetProperty("savedInCents").GetInt64());
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Equal(2, suggestions.Count);
        Assert.Equal("unallocatedMoney", suggestions[0].GetProperty("rule").GetString());
        Assert.Equal(100_000, suggestions[0].GetProperty("monthlySavingsInCents").GetInt32());
        Assert.Equal("insufficientSavings", suggestions[1].GetProperty("rule").GetString());
        Assert.Equal(10_000, suggestions[1].GetProperty("monthlySavingsInCents").GetInt32());
        Assert.Equal(JsonValueKind.Null, suggestions[1].GetProperty("categoryId").ValueKind);
        Assert.Equal(October, user.Account.LastSuggestionMonth);
    }

    // La règle « dépassement » donne sa catégorie et son nom ; une suggestion d'un autre n'apparaît jamais (SS §4).
    [Fact]
    public async Task ListGivesTheCategoryOfAnOverrunAndOnlyOwnSuggestions()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 0);
        var other = await AddAccountAsync(api, OtherUserEmail);
        user.Account.NoteSuggestionMonth(October);
        var food = api.CategoryRepository.SavedCategories.Single(category => category.UserId == user.Account.Id && category.Name == "Alimentation");
        var overrun = SaveSuggestion(api, user.Account, SuggestionRule.CategoryOverrun, 3_000, food.Id);
        SaveSuggestion(api, other, SuggestionRule.UnallocatedMoney, 9_000);

        var body = await ReadJsonAsync(await user.Client.GetAsync(SuggestionsEndpoint));

        var only = Assert.Single(body.GetProperty("suggestions").EnumerateArray());
        Assert.Equal(overrun.Id, only.GetProperty("id").GetGuid());
        Assert.Equal("categoryOverrun", only.GetProperty("rule").GetString());
        Assert.Equal(food.Id, only.GetProperty("categoryId").GetGuid());
        Assert.Equal("Alimentation", only.GetProperty("categoryName").GetString());
        Assert.Equal("2026-10-01", only.GetProperty("month").GetString());
    }

    // --- Accepter

    // Accepter place l'argent dans le sac et répond 201 avec le mouvement ; les économies réalisées le comptent ensuite.
    [Fact]
    public async Task AcceptingPlacesTheMoneyInTheBag()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        user.Account.NoteSuggestionMonth(October);
        var bag = SaveBag(api, user.Account, "Épargne");
        var suggestion = SaveSuggestion(api, user.Account, SuggestionRule.UnallocatedMoney, 30_000);

        var response = await AcceptAsync(user, suggestion.Id, bag.Id, 25_000);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(25_000, body.GetProperty("amountInCents").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sourceBagId").ValueKind);
        Assert.Equal(bag.Id, body.GetProperty("destinationBagId").GetGuid());
        Assert.Equal(SuggestionStatus.Accepted, suggestion.Status);
        Assert.Equal(suggestion.Id, Assert.Single(api.BagRepository.SavedMovements).SavingsSuggestionId);
        Assert.Equal(25_000, (await ReadJsonAsync(await user.Client.GetAsync(SuggestionsEndpoint))).GetProperty("savedInCents").GetInt64());
    }

    // Les refus : un montant hors limites, l'argent disponible qui ne suffit pas, une suggestion déjà répondue,
    // et un champ manquant. Aucun ne déplace d'argent par erreur.
    [Fact]
    public async Task AcceptanceRefusalsExplainWhy()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 20_000);
        var bag = SaveBag(api, user.Account, "Épargne");
        var suggestion = SaveSuggestion(api, user.Account, SuggestionRule.UnallocatedMoney, 30_000);

        var tooBig = await AcceptAsync(user, suggestion.Id, bag.Id, 30_001);
        var tooLow = await AcceptAsync(user, suggestion.Id, bag.Id, 25_000);
        var missingBag = await SendAsync(user.Client, HttpMethod.Post, AcceptAddress(suggestion.Id), new { amountInCents = 1_000 }, user.CsrfToken);
        var missingAmount = await SendAsync(user.Client, HttpMethod.Post, AcceptAddress(suggestion.Id), new { bagId = bag.Id }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal(ErrorMessages.SuggestionAmountBreaksRules, await ReadFieldErrorAsync(tooBig, nameof(SuggestionAcceptanceRequest.AmountInCents)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);
        Assert.Equal(ErrorMessages.AvailableMoneyTooLow, await ReadFieldErrorAsync(tooLow, nameof(SuggestionAcceptanceRequest.AmountInCents)));
        Assert.Equal(HttpStatusCode.BadRequest, missingBag.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingAmount.StatusCode);
        Assert.Empty(api.BagRepository.SavedMovements);

        Assert.Equal(HttpStatusCode.Created, (await AcceptAsync(user, suggestion.Id, bag.Id, 20_000)).StatusCode);
        await AssertProblemTitleAsync(await AcceptAsync(user, suggestion.Id, bag.Id, 1), HttpStatusCode.Conflict, ErrorMessages.SuggestionAlreadyAnswered);
        await AssertProblemTitleAsync(await IgnoreAsync(user, suggestion.Id), HttpStatusCode.Conflict, ErrorMessages.SuggestionAlreadyAnswered);
        Assert.Single(api.BagRepository.SavedMovements);
    }

    // --- Ignorer

    [Fact]
    public async Task IgnoringAnswersWithoutContent()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 0);
        var suggestion = SaveSuggestion(api, user.Account, SuggestionRule.InsufficientSavings, 5_000);

        var response = await IgnoreAsync(user, suggestion.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(SuggestionStatus.Ignored, suggestion.Status);
        Assert.Equal(api.Clock.GetUtcNow().UtcDateTime, suggestion.StatusChangedAt);
    }

    // --- Contrôle d'accès (SS §4 et §11)

    // La suggestion ou le sac d'un autre répond 404, comme un identifiant inconnu, et l'essai est noté au journal.
    // Un identifiant inconnu n'est pas noté : personne ne possède la ressource.
    [Fact]
    public async Task ResourcesOfAnotherAreNotFoundAndJournaled()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var other = await AddAccountAsync(api, OtherUserEmail);
        var ownBag = SaveBag(api, user.Account, "Épargne");
        var ownSuggestion = SaveSuggestion(api, user.Account, SuggestionRule.UnallocatedMoney, 30_000);
        var suggestionOfOther = SaveSuggestion(api, other, SuggestionRule.UnallocatedMoney, 30_000);
        var bagOfOther = SaveBag(api, other, "Épargne");

        var acceptOther = await AcceptAsync(user, suggestionOfOther.Id, ownBag.Id, 1_000);
        var ignoreOther = await IgnoreAsync(user, suggestionOfOther.Id);
        var intoOtherBag = await AcceptAsync(user, ownSuggestion.Id, bagOfOther.Id, 1_000);
        var unknown = await IgnoreAsync(user, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, acceptOther.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ignoreOther.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, intoOtherBag.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(3, CountAccessDenied(api, user.Account));
        Assert.Equal(SuggestionStatus.Proposed, suggestionOfOther.Status);
        Assert.Equal(SuggestionStatus.Proposed, ownSuggestion.Status);
        Assert.Empty(api.BagRepository.SavedMovements);
    }

    // --- Limites

    // Chaque réponse à une suggestion limite la taille de sa requête, et une acceptation tient largement dans cette limite.
    [Fact]
    public void AnswerRoutesLimitTheirRequestSize()
    {
        foreach (var methodName in new[] { nameof(SuggestionsController.Accept), nameof(SuggestionsController.Ignore) })
        {
            var limit = typeof(SuggestionsController).GetMethod(methodName)!.GetCustomAttribute<RequestSizeLimitAttribute>();
            Assert.NotNull(limit);
        }

        var json = "{\"bagId\":\"" + Guid.NewGuid() + "\",\"amountInCents\":1000000000}";
        Assert.True(Encoding.UTF8.GetByteCount(json) * 6 <= RequestSizeConfiguration.MaximumSuggestionRequestBytes);
    }
}
