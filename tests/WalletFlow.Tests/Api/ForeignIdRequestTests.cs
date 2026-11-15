using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie, pour chaque identifiant qu'une requête peut envoyer, que l'identifiant d'une ressource d'un autre utilisateur reçoit
// la même réponse qu'un identifiant qui n'existe pas (SS §4), ne change rien, et laisse une trace au journal de sécurité (SS §11).
// L'inventaire lit dans l'API tous les identifiants reçus, dans l'adresse, la requête ou le corps : une nouvelle route, ou un
// nouveau champ d'identifiant, sans son cas fait échouer le test, comme un cas qui ne vise plus rien. Les routes et les noms
// sont écrits en toutes lettres, mais comparés exactement à ceux de l'API : un renommage fait échouer le test, sans le vider.
public class ForeignIdRequestTests
{
    private const string OtherUserEmail = "autre-utilisateur@exemple.test";
    private const string ChartPeriod = "from=2026-11-01&to=2026-11-30";
    private static readonly DateOnly September = new DateOnly(2026, 9, 1);

    // Les ressources d'un utilisateur, une de chaque sorte qu'une requête peut viser.
    private sealed record OwnedIds(Guid Category, Guid Transaction, Guid Connection, Guid BankAccount, Guid Bag, Guid Suggestion);

    // Un utilisateur connecté : son navigateur, son jeton anti-CSRF, son compte et ses ressources.
    private sealed record LoggedInUser(HttpClient Client, string CsrfToken, User Account, OwnedIds Ids);

    // Une requête à envoyer : sa méthode, son adresse et son corps JSON s'il en a un.
    private sealed record ApiRequest(HttpMethod Method, string Address, object? Body);

    // Un identifiant reçu par une route : la route telle que l'API la déclare, le nom de l'identifiant, la ressource de l'autre
    // qu'il vise, la requête qui l'envoie (ses autres identifiants sont ceux du visiteur), et le code attendu, le même pour
    // la ressource de l'autre et pour un identifiant inconnu.
    private sealed record ForeignIdCase(
        string Route,
        string IdName,
        Func<OwnedIds, Guid> TargetOf,
        Func<OwnedIds, Guid, ApiRequest> RequestFor,
        HttpStatusCode ExpectedStatus);

    private static readonly List<ForeignIdCase> Cases = new List<ForeignIdCase>
    {
        NotFound("PUT api/categories/{categoryId:guid}/name", "categoryId", ids => ids.Category,
            (own, id) => Put("/api/categories/" + id + "/name", new { name = "Piraté" })),
        NotFound("PUT api/categories/{categoryId:guid}/budget", "categoryId", ids => ids.Category,
            (own, id) => Put("/api/categories/" + id + "/budget", new { monthlyBudgetInCents = 1 })),
        NotFound("DELETE api/categories/{categoryId:guid}", "categoryId", ids => ids.Category,
            (own, id) => Delete("/api/categories/" + id)),
        EmptyResult("GET api/transactions", "accountId", ids => ids.BankAccount,
            (own, id) => Get("/api/transactions?accountId=" + id)),
        EmptyResult("GET api/transactions", "categoryId", ids => ids.Category,
            (own, id) => Get("/api/transactions?categoryId=" + id)),
        NotFound("PUT api/transactions/{transactionId:guid}/category", "transactionId", ids => ids.Transaction,
            (own, id) => Put("/api/transactions/" + id + "/category", new { categoryId = own.Category, applyToFutureTransactions = true })),
        NotFound("PUT api/transactions/{transactionId:guid}/category", "categoryId", ids => ids.Category,
            (own, id) => Put("/api/transactions/" + own.Transaction + "/category", new { categoryId = id, applyToFutureTransactions = true })),
        NotFound("PUT api/transactions/{transactionId:guid}/transfer", "transactionId", ids => ids.Transaction,
            (own, id) => Put("/api/transactions/" + id + "/transfer", new { isInternalTransfer = true })),
        EmptyResult("GET api/charts/categories", "accountId", ids => ids.BankAccount,
            (own, id) => Get("/api/charts/categories?" + ChartPeriod + "&accountId=" + id)),
        EmptyResult("GET api/charts/months", "accountId", ids => ids.BankAccount,
            (own, id) => Get("/api/charts/months?" + ChartPeriod + "&accountId=" + id)),
        EmptyResult("GET api/charts/comparison", "accountId", ids => ids.BankAccount,
            (own, id) => Get("/api/charts/comparison?accountId=" + id)),
        NotFound("PUT api/bags/{bagId:guid}/name", "bagId", ids => ids.Bag,
            (own, id) => Put("/api/bags/" + id + "/name", new { name = "Piraté" })),
        NotFound("PUT api/bags/{bagId:guid}/goal", "bagId", ids => ids.Bag,
            (own, id) => Put("/api/bags/" + id + "/goal", new { targetAmountInCents = 1, targetDate = (DateOnly?)null })),
        NotFound("DELETE api/bags/{bagId:guid}", "bagId", ids => ids.Bag,
            (own, id) => Delete("/api/bags/" + id)),
        NotFound("GET api/bags/{bagId:guid}/movements", "bagId", ids => ids.Bag,
            (own, id) => Get("/api/bags/" + id + "/movements")),
        NotFound("POST api/bags/movements", "sourceBagId", ids => ids.Bag,
            (own, id) => Post("/api/bags/movements", new { sourceBagId = id, destinationBagId = own.Bag, amountInCents = 1 })),
        NotFound("POST api/bags/movements", "destinationBagId", ids => ids.Bag,
            (own, id) => Post("/api/bags/movements", new { sourceBagId = own.Bag, destinationBagId = id, amountInCents = 1 })),
        NotFound("POST api/suggestions/{suggestionId:guid}/accept", "suggestionId", ids => ids.Suggestion,
            (own, id) => Post("/api/suggestions/" + id + "/accept", new { bagId = own.Bag, amountInCents = 1_000 })),
        NotFound("POST api/suggestions/{suggestionId:guid}/accept", "bagId", ids => ids.Bag,
            (own, id) => Post("/api/suggestions/" + own.Suggestion + "/accept", new { bagId = id, amountInCents = 1_000 })),
        NotFound("POST api/suggestions/{suggestionId:guid}/ignore", "suggestionId", ids => ids.Suggestion,
            (own, id) => Post("/api/suggestions/" + id + "/ignore", null)),
        NotFound("POST api/bank-connections/{connectionId:guid}/renew", "connectionId", ids => ids.Connection,
            (own, id) => Post("/api/bank-connections/" + id + "/renew", null)),
        NotFound("DELETE api/bank-connections/{connectionId:guid}", "connectionId", ids => ids.Connection,
            (own, id) => Delete("/api/bank-connections/" + id))
    };

    // Les noms des cas, pour que chaque identifiant apparaisse comme un test à part.
    public static TheoryData<string> CaseNames()
    {
        var names = new TheoryData<string>();

        foreach (var testCase in Cases)
        {
            names.Add(NameOf(testCase.Route, testCase.IdName));
        }

        return names;
    }

    // Un cas où la ressource de l'autre répond « introuvable », comme une adresse qui n'existe pas.
    private static ForeignIdCase NotFound(string route, string idName, Func<OwnedIds, Guid> targetOf, Func<OwnedIds, Guid, ApiRequest> requestFor)
    {
        return new ForeignIdCase(route, idName, targetOf, requestFor, HttpStatusCode.NotFound);
    }

    // Un cas de filtre : la ressource de l'autre ne filtre rien de visible, la liste ou le graphique est vide.
    private static ForeignIdCase EmptyResult(string route, string idName, Func<OwnedIds, Guid> targetOf, Func<OwnedIds, Guid, ApiRequest> requestFor)
    {
        return new ForeignIdCase(route, idName, targetOf, requestFor, HttpStatusCode.OK);
    }

    // Une lecture à cette adresse.
    private static ApiRequest Get(string address)
    {
        return new ApiRequest(HttpMethod.Get, address, null);
    }

    // Un remplacement à cette adresse, avec ce corps.
    private static ApiRequest Put(string address, object body)
    {
        return new ApiRequest(HttpMethod.Put, address, body);
    }

    // Une action à cette adresse, avec ce corps s'il y en a un.
    private static ApiRequest Post(string address, object? body)
    {
        return new ApiRequest(HttpMethod.Post, address, body);
    }

    // Une suppression à cette adresse.
    private static ApiRequest Delete(string address)
    {
        return new ApiRequest(HttpMethod.Delete, address, null);
    }

    // Nomme un cas par sa route et son identifiant.
    private static string NameOf(string route, string idName)
    {
        return route + " → " + idName;
    }

    // L'identifiant d'une ressource de l'autre reçoit mot pour mot la réponse d'un identifiant inconnu. Aucune des deux requêtes
    // ne change ce que l'un ou l'autre voit, ni ne sort de l'API, et seule la première est notée au journal, au nom du visiteur.
    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task ForeignIdAnswersLikeAnUnknownIdAndIsJournaled(string caseName)
    {
        var testCase = Cases.Single(candidate => NameOf(candidate.Route, candidate.IdName) == caseName);
        using var api = new ApiFactoryWithFakes();
        var owner = await LogInWithResourcesAsync(api, Email, "stable-1");
        var visitor = await LogInWithResourcesAsync(api, OtherUserEmail, "stable-2");
        var ownerView = await ReadViewAsync(owner);
        var visitorView = await ReadViewAsync(visitor);
        var outsideEffects = CountOutsideEffects(api);
        var target = testCase.TargetOf(owner.Ids);
        var foreignRequest = testCase.RequestFor(visitor.Ids, target);

        var foreign = await SendAsync(visitor, foreignRequest);
        var unknown = await SendAsync(visitor, testCase.RequestFor(visitor.Ids, Guid.NewGuid()));

        Assert.Equal(target.ToString(), ReadSentValue(foreignRequest, testCase.Route, testCase.IdName));
        Assert.Equal(testCase.ExpectedStatus, foreign.StatusCode);
        Assert.Equal(await ReadVisibleAnswerAsync(unknown), await ReadVisibleAnswerAsync(foreign));
        Assert.Equal(1, CountAccessDenied(api, visitor.Account));
        Assert.Equal(0, CountAccessDenied(api, owner.Account));
        Assert.Equal(ownerView, await ReadViewAsync(owner));
        Assert.Equal(visitorView, await ReadViewAsync(visitor));
        Assert.Equal(outsideEffects, CountOutsideEffects(api));
    }

    // Chaque identifiant qu'une route reçoit a son cas, et chaque cas vise un identifiant qu'une route reçoit encore.
    [Fact]
    public void EveryIdReceivedByARouteHasItsCase()
    {
        var received = ReadIdsReceivedByRoutes();
        var covered = Cases.Select(testCase => NameOf(testCase.Route, testCase.IdName)).ToList();

        received.Sort(StringComparer.Ordinal);
        covered.Sort(StringComparer.Ordinal);

        Assert.Equal(received, covered);
    }

    // Lit dans l'API chaque identifiant qu'une requête peut envoyer, nommé comme un cas.
    private static List<string> ReadIdsReceivedByRoutes()
    {
        using var api = new ApiFactoryWithFakes();
        var routeProvider = api.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var received = new List<string>();

        foreach (var route in routeProvider.ActionDescriptors.Items)
        {
            var httpMethod = route.EndpointMetadata.OfType<IHttpMethodMetadata>().SelectMany(metadata => metadata.HttpMethods).Single();

            foreach (var parameter in route.Parameters)
            {
                foreach (var idName in ReadIdNames(parameter.Name, parameter.ParameterType))
                {
                    received.Add(NameOf(httpMethod + " " + route.AttributeRouteInfo!.Template, idName));
                }
            }
        }

        return received;
    }

    // Donne les identifiants que porte une valeur reçue sous ce nom : elle-même si c'est un identifiant ou une liste
    // d'identifiants, sinon ceux de ses propriétés, nommés comme le JSON et l'adresse les écrivent. Seules les requêtes
    // de l'API sont ouvertes : un type du framework n'en porte pas.
    private static List<string> ReadIdNames(string name, Type type)
    {
        if (IsIdOrIdList(type))
        {
            return new List<string> { name };
        }

        if (type.Assembly != typeof(Program).Assembly)
        {
            return new List<string>();
        }

        return type.GetProperties()
            .SelectMany(property => ReadIdNames(JsonNamingPolicy.CamelCase.ConvertName(property.Name), property.PropertyType))
            .ToList();
    }

    // Vrai pour un identifiant, facultatif ou non, ou pour une liste d'identifiants.
    private static bool IsIdOrIdList(Type type)
    {
        var listElement = type.GetInterfaces()
            .Append(type)
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .FirstOrDefault();
        var element = listElement ?? type;

        return (Nullable.GetUnderlyingType(element) ?? element) == typeof(Guid);
    }

    // Lit la valeur que la requête envoie sous ce nom, là où la route l'attend : dans l'adresse, dans la requête ou dans le corps.
    // Renvoie null si la requête ne va pas vers cette route, ou n'envoie rien sous ce nom : le cas ne viserait pas ce qu'il dit.
    private static string? ReadSentValue(ApiRequest request, string route, string idName)
    {
        var methodAndTemplate = route.Split(' ');
        var address = new Uri(new Uri("https://localhost"), request.Address);
        var pattern = "^/" + Regex.Replace(Regex.Escape(methodAndTemplate[1]), @"\\\{(\w+):guid}", "(?<$1>[^/]+)") + "$";
        var match = Regex.Match(address.AbsolutePath, pattern);

        if (request.Method.Method != methodAndTemplate[0] || !match.Success)
        {
            return null;
        }

        if (match.Groups[idName].Success)
        {
            return match.Groups[idName].Value;
        }

        if (QueryHelpers.ParseQuery(address.Query).TryGetValue(idName, out var queryValue))
        {
            return queryValue.ToString();
        }

        if (request.Body != null && JsonSerializer.SerializeToElement(request.Body).TryGetProperty(idName, out var bodyValue))
        {
            return bodyValue.ToString();
        }

        return null;
    }

    // Ajoute un compte avec ses catégories par défaut et le connecte. Lui connecte par l'API une banque avec une opération,
    // puis lui garde un sac de 10 000 centimes et une suggestion de septembre. Septembre est noté comme calculé : le 4 novembre
    // de la fausse horloge, la liste des suggestions le montre sans rien recalculer.
    private static async Task<LoggedInUser> LogInWithResourcesAsync(ApiFactoryWithFakes api, string email, string stableId)
    {
        var account = await AddAccountAsync(api, email);
        account.NoteSuggestionMonth(September);
        api.CategoryRepository.SavedCategories.AddRange(DefaultCategories.CreateForUser(account.Id));
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client, email);

        api.BankProvider.AddTransaction(stableId, new DateOnly(2026, 11, 2), -4_520, "CB CARREFOUR");
        var start = await SendAsync(client, csrfToken, Post("/api/bank-connections", new { bankName = FakeBankDataProvider.BankName }));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var code = "code-" + stableId;
        api.BankProvider.AddSession(code, new TestBankAccount(stableId, "Compte courant", "EUR", 152_034));
        var finishRequest = Post("/api/bank-connections/finish", new { code = code, state = api.BankProvider.AuthorizationRequests.Last().State });
        var finish = await SendAsync(client, csrfToken, finishRequest);
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        var connectionId = (await ReadJsonAsync(finish)).GetProperty("connectionId").GetGuid();
        var transaction = (await ReadJsonAsync(await client.GetAsync("/api/transactions"))).GetProperty("items").EnumerateArray().Single();

        var bag = new Bag { Name = "Vacances", UserId = account.Id };
        api.BagRepository.SavedBags.Add(bag);
        api.BagRepository.SavedMovements.Add(new BagMovement
        {
            AmountInCents = 10_000,
            MovedAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-1),
            DestinationBagId = bag.Id
        });
        var suggestion = new SavingsSuggestion
        {
            Rule = SuggestionRule.UnallocatedMoney,
            Month = September,
            MonthlySavingsInCents = 3_000,
            UserId = account.Id
        };
        api.SuggestionRepository.SavedSuggestions.Add(suggestion);

        var leisure = api.CategoryRepository.SavedCategories.Single(category => category.UserId == account.Id && category.Name == "Loisirs");
        var ids = new OwnedIds(
            leisure.Id,
            transaction.GetProperty("id").GetGuid(),
            connectionId,
            transaction.GetProperty("accountId").GetGuid(),
            bag.Id,
            suggestion.Id);

        return new LoggedInUser(client, csrfToken, account, ids);
    }

    // Envoie cette requête depuis le navigateur de cet utilisateur, avec son jeton anti-CSRF.
    private static async Task<HttpResponseMessage> SendAsync(LoggedInUser user, ApiRequest request)
    {
        return await SendAsync(user.Client, user.CsrfToken, request);
    }

    // Envoie cette requête depuis ce navigateur, avec ce jeton anti-CSRF.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string csrfToken, ApiRequest request)
    {
        var message = new HttpRequestMessage(request.Method, request.Address);

        if (request.Body != null)
        {
            message.Content = JsonContent.Create(request.Body);
        }

        message.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);

        return await client.SendAsync(message);
    }

    // Lit le corps JSON d'une réponse.
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Lit tout ce que l'utilisateur voit de ses données, pour vérifier qu'une requête n'y a rien changé.
    private static async Task<string> ReadViewAsync(LoggedInUser user)
    {
        var addresses = new List<string>
        {
            "/api/categories", "/api/transactions", "/api/bank-connections", "/api/balance", "/api/bags",
            "/api/bags/" + user.Ids.Bag + "/movements", "/api/suggestions"
        };
        var view = new StringBuilder();

        foreach (var address in addresses)
        {
            var response = await user.Client.GetAsync(address);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            view.AppendLine(address + " " + await response.Content.ReadAsStringAsync());
        }

        return view.ToString();
    }

    // Lit la réponse telle que le visiteur la reçoit : code, type et corps, sans le traceId propre à chaque requête.
    private static async Task<string> ReadVisibleAnswerAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        if (body.Length > 0 && JsonNode.Parse(body) is JsonObject json)
        {
            json.Remove("traceId");
            body = json.ToJsonString();
        }

        return (int)response.StatusCode + " " + response.Content.Headers.ContentType + " " + body;
    }

    // Compte les accès refusés notés au journal pour ce compte (SS §11).
    private static int CountAccessDenied(ApiFactoryWithFakes api, User account)
    {
        return api.SecurityEventRepository.SavedSecurityEvents.Count(securityEvent =>
            securityEvent.Type == SecurityEventType.AccessDenied
            && securityEvent.Result == SecurityEventResult.Failure
            && securityEvent.UserId == account.Id);
    }

    // Compte ce qui est sorti de l'API : demandes au prestataire bancaire et e-mails envoyés.
    private static (int Authorizations, int Revocations, int Readings, int Emails) CountOutsideEffects(ApiFactoryWithFakes api)
    {
        return (
            api.BankProvider.AuthorizationRequests.Count,
            api.BankProvider.RevokedSessions.Count,
            api.BankProvider.TransactionReadings.Count,
            api.EmailSender.SentEmails.Count);
    }
}
