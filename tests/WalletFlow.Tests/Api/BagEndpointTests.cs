using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Application.Bags;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes des sacs (GR Sacs, SS §4, §5 et §6) dans la vraie API, avec une fausse base.
public class BagEndpointTests
{
    private const string BagsEndpoint = "/api/bags";
    private const string MovementsEndpoint = "/api/bags/movements";
    private const string OtherUserEmail = "autre-utilisateur@exemple.test";

    // Un utilisateur connecté : son navigateur, son jeton anti-CSRF et son compte.
    private sealed record LoggedInUser(HttpClient Client, string CsrfToken, User Account);

    // Ajoute un compte, le connecte dans un nouveau navigateur, et lui donne un compte bancaire avec ce solde.
    private static async Task<LoggedInUser> LogInWithBalanceAsync(ApiFactoryWithFakes api, int balanceInCents, string email = Email)
    {
        var account = await AddAccountAsync(api, email);
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

    // Garde directement un sac de ce compte, avec ce solde venu de l'argent disponible.
    private static Bag SaveBag(ApiFactoryWithFakes api, User account, string name, int balanceInCents = 0)
    {
        var bag = new Bag { Name = name, UserId = account.Id };
        api.BagRepository.SavedBags.Add(bag);

        if (balanceInCents > 0)
        {
            api.BagRepository.SavedMovements.Add(new BagMovement
            {
                AmountInCents = balanceInCents,
                MovedAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-2),
                DestinationBagId = bag.Id
            });
        }

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

    // Envoie ce texte JSON tel quel : certains corps ne s'écrivent pas avec un objet C#.
    private static async Task<HttpResponseMessage> SendRawAsync(LoggedInUser user, HttpMethod method, string address, string json)
    {
        var request = new HttpRequestMessage(method, address)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, user.CsrfToken);

        return await user.Client.SendAsync(request);
    }

    // Adresse de ce sac.
    private static string BagAddress(Guid bagId)
    {
        return BagsEndpoint + "/" + bagId;
    }

    // Lit le corps JSON d'une réponse.
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Lit le message d'erreur de ce champ dans une réponse refusée.
    private static async Task<string?> ReadFieldErrorAsync(HttpResponseMessage response, string field)
    {
        var body = await ReadJsonAsync(response);

        return body.GetProperty("errors").GetProperty(field)[0].GetString();
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
    public async Task EveryBagRouteNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        var anyId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(BagsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, BagsEndpoint, new { name = "Vacances" }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Put, BagAddress(anyId) + "/name", new { name = "Voyage" }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Put, BagAddress(anyId) + "/goal", new { targetAmountInCents = 1, targetDate = (DateOnly?)null }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Delete, BagAddress(anyId), null, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = (Guid?)null, destinationBagId = anyId, amountInCents = 1 }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(BagAddress(anyId) + "/movements")).StatusCode);
    }

    // SS §6 : toute requête qui modifie des données demande le jeton anti-CSRF. Sans lui, rien ne change.
    [Fact]
    public async Task WriteRoutesWithoutCsrfTokenChangeNothing()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances", 5000);

        var creation = await SendAsync(user.Client, HttpMethod.Post, BagsEndpoint, new { name = "Épargne" }, null);
        var rename = await SendAsync(user.Client, HttpMethod.Put, BagAddress(holidays.Id) + "/name", new { name = "Voyage" }, null);
        var goal = await SendAsync(user.Client, HttpMethod.Put, BagAddress(holidays.Id) + "/goal", new { targetAmountInCents = 1, targetDate = (DateOnly?)null }, null);
        var movement = await SendAsync(user.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = (Guid?)null, destinationBagId = holidays.Id, amountInCents = 1 }, null);
        var deletion = await SendAsync(user.Client, HttpMethod.Delete, BagAddress(holidays.Id), null, null);

        Assert.Equal(HttpStatusCode.BadRequest, creation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, goal.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, movement.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, deletion.StatusCode);
        Assert.Equal(new List<Bag> { holidays }, api.BagRepository.SavedBags);
        Assert.Equal("Vacances", holidays.Name);
        Assert.Null(holidays.TargetAmountInCents);
        Assert.Single(api.BagRepository.SavedMovements);
    }

    // --- Liste

    // Ce sont des données financières : aucun navigateur ni intermédiaire ne doit les garder.
    [Fact]
    public async Task ListGivesTheBagsAndTheTotalsAndIsNeverCached()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances", 30_000);
        holidays.ReplaceGoal(60_000, new DateOnly(2027, 7, 1), new DateOnly(2026, 11, 4));
        SaveBag(api, user.Account, "Épargne", 10_000);

        var response = await user.Client.GetAsync(BagsEndpoint);
        var body = await ReadJsonAsync(response);
        var bags = body.GetProperty("bags").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(new List<string> { "Épargne", "Vacances" }, bags.Select(bag => bag.GetProperty("name").GetString()!).ToList());
        Assert.Equal(holidays.Id, bags[1].GetProperty("id").GetGuid());
        Assert.Equal(30_000, bags[1].GetProperty("balanceInCents").GetInt64());
        Assert.Equal(60_000, bags[1].GetProperty("targetAmountInCents").GetInt32());
        Assert.Equal("2027-07-01", bags[1].GetProperty("targetDate").GetString());
        Assert.Equal(50, bags[1].GetProperty("progressPercent").GetInt32());
        Assert.Equal(JsonValueKind.Null, bags[0].GetProperty("progressPercent").ValueKind);
        Assert.Equal(100_000, body.GetProperty("totalBalanceInCents").GetInt64());
        Assert.Equal(40_000, body.GetProperty("bagsTotalInCents").GetInt64());
        Assert.Equal(60_000, body.GetProperty("availableInCents").GetInt64());
        Assert.Equal(api.Clock.GetUtcNow().UtcDateTime, body.GetProperty("oldestBalanceUpdatedAt").GetDateTime());
    }

    // --- Création

    [Fact]
    public async Task CreationAnswersCreatedWithTheNewBag()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);

        var response = await SendAsync(user.Client, HttpMethod.Post, BagsEndpoint, new { name = " Vacances ", targetAmountInCents = 150_000, targetDate = "2027-07-01" }, user.CsrfToken);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var savedBag = Assert.Single(api.BagRepository.SavedBags);
        Assert.Equal(savedBag.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Vacances", body.GetProperty("name").GetString());
        Assert.Equal(0, body.GetProperty("balanceInCents").GetInt64());
        Assert.Equal(150_000, body.GetProperty("targetAmountInCents").GetInt32());
        Assert.Equal("2027-07-01", body.GetProperty("targetDate").GetString());
        Assert.Equal(0, body.GetProperty("progressPercent").GetInt32());
        Assert.Equal(user.Account.Id, savedBag.UserId);
    }

    // Chaque refus est donné sur le champ qui ne va pas. Une date sans montant est refusée sur le montant : c'est lui qui manque.
    [Fact]
    public async Task CreationRefusalsAreGivenOnTheRightField()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);

        var name = await SendAsync(user.Client, HttpMethod.Post, BagsEndpoint, new { name = "  " }, user.CsrfToken);
        var amount = await SendAsync(user.Client, HttpMethod.Post, BagsEndpoint, new { name = "Vacances", targetAmountInCents = 0 }, user.CsrfToken);
        var dateAlone = await SendAsync(user.Client, HttpMethod.Post, BagsEndpoint, new { name = "Vacances", targetDate = "2027-07-01" }, user.CsrfToken);
        var today = await SendAsync(user.Client, HttpMethod.Post, BagsEndpoint, new { name = "Vacances", targetAmountInCents = 500, targetDate = "2026-11-04" }, user.CsrfToken);

        Assert.Equal(ErrorMessages.BagNameBreaksRules, await ReadFieldErrorAsync(name, "Name"));
        Assert.Equal(ErrorMessages.BagGoalAmountBreaksRules, await ReadFieldErrorAsync(amount, "TargetAmountInCents"));
        Assert.Equal(ErrorMessages.BagGoalDateWithoutAmount, await ReadFieldErrorAsync(dateAlone, "TargetAmountInCents"));
        Assert.Equal(ErrorMessages.BagGoalDateTooEarly, await ReadFieldErrorAsync(today, "TargetDate"));
        Assert.Empty(api.BagRepository.SavedBags);
    }

    // SS §6 : le cent unième sac est refusé avec un conflit, et son message.
    [Fact]
    public async Task HundredAndFirstBagIsRefusedWithAConflict()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);

        for (var number = 1; number <= 100; number++)
        {
            SaveBag(api, user.Account, "Sac " + number);
        }

        var response = await SendAsync(user.Client, HttpMethod.Post, BagsEndpoint, new { name = "Sac 101" }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorMessages.TooManyBags, (await ReadJsonAsync(response)).GetProperty("title").GetString());
        Assert.Equal(100, api.BagRepository.SavedBags.Count);
    }

    // SS §4 : seuls les champs prévus sont lus. Un utilisateur ou un solde ajouté à la requête est ignoré.
    [Fact]
    public async Task FieldsAddedToTheCreationAreIgnored()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var otherUser = await AddAccountAsync(api, OtherUserEmail);

        var response = await SendAsync(user.Client, HttpMethod.Post, BagsEndpoint, new { name = "Vacances", userId = otherUser.Id, balanceInCents = 99_999, id = Guid.Empty }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var savedBag = Assert.Single(api.BagRepository.SavedBags);
        Assert.Equal(user.Account.Id, savedBag.UserId);
        Assert.NotEqual(Guid.Empty, savedBag.Id);
        Assert.Empty(api.BagRepository.SavedMovements);
    }

    // --- Nom et objectif

    [Fact]
    public async Task RenameAnswersTheRenamedBag()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances", 5000);

        var response = await SendAsync(user.Client, HttpMethod.Put, BagAddress(holidays.Id) + "/name", new { name = "Voyage" }, user.CsrfToken);
        var refused = await SendAsync(user.Client, HttpMethod.Put, BagAddress(holidays.Id) + "/name", new { name = new string('a', 51) }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Voyage", (await ReadJsonAsync(response)).GetProperty("name").GetString());
        Assert.Equal(5000, (await ReadJsonAsync(response)).GetProperty("balanceInCents").GetInt64());
        Assert.Equal(ErrorMessages.BagNameBreaksRules, await ReadFieldErrorAsync(refused, "Name"));
        Assert.Equal("Voyage", holidays.Name);
    }

    [Fact]
    public async Task GoalIsSetThenRemoved()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances", 25_000);

        var set = await SendAsync(user.Client, HttpMethod.Put, BagAddress(holidays.Id) + "/goal", new { targetAmountInCents = 100_000, targetDate = "2027-01-01" }, user.CsrfToken);
        var setBody = await ReadJsonAsync(set);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(25, setBody.GetProperty("progressPercent").GetInt32());
        Assert.Equal(new DateOnly(2027, 1, 1), holidays.TargetDate);

        var removed = await SendAsync(user.Client, HttpMethod.Put, BagAddress(holidays.Id) + "/goal", new { targetAmountInCents = (int?)null, targetDate = (DateOnly?)null }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadJsonAsync(removed)).GetProperty("progressPercent").ValueKind);
        Assert.Null(holidays.TargetAmountInCents);
        Assert.Null(holidays.TargetDate);
    }

    // Les deux champs doivent être écrits, même à null : une requête vide ou incomplète n'enlève jamais un objectif par erreur.
    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "targetAmountInCents": null }""")]
    [InlineData("""{ "targetDate": null }""")]
    public async Task IncompleteGoalRequestKeepsTheGoal(string json)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances");
        holidays.ReplaceGoal(100_000, null, new DateOnly(2026, 11, 4));

        var response = await SendRawAsync(user, HttpMethod.Put, BagAddress(holidays.Id) + "/goal", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(100_000, holidays.TargetAmountInCents);
    }

    // SS §5 : un montant est un nombre entier de centimes, et une date s'écrit comme 2027-07-01.
    [Theory]
    [InlineData("""{ "targetAmountInCents": 12.5, "targetDate": null }""")]
    [InlineData("""{ "targetAmountInCents": "3000", "targetDate": null }""")]
    [InlineData("""{ "targetAmountInCents": 3000000000, "targetDate": null }""")]
    [InlineData("""{ "targetAmountInCents": 3000, "targetDate": "01/07/2027" }""")]
    [InlineData("""{ "targetAmountInCents": 3000, "targetDate": "2027-02-30" }""")]
    public async Task GoalThatIsNotWellWrittenIsRefused(string json)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances");

        var response = await SendRawAsync(user, HttpMethod.Put, BagAddress(holidays.Id) + "/goal", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(holidays.TargetAmountInCents);
    }

    // --- Suppression

    [Fact]
    public async Task DeletionAnswersAMessageAndGivesTheMoneyBack()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances", 40_000);

        var response = await SendAsync(user.Client, HttpMethod.Delete, BagAddress(holidays.Id), null, user.CsrfToken);
        var list = await ReadJsonAsync(await user.Client.GetAsync(BagsEndpoint));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BagsController.BagDeletedMessage, (await ReadJsonAsync(response)).GetProperty("message").GetString());
        Assert.Empty(api.BagRepository.SavedBags);
        Assert.Equal(100_000, list.GetProperty("availableInCents").GetInt64());
    }

    // --- Déplacements

    [Fact]
    public async Task MovementAnswersCreatedWithTheMovement()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances");

        var response = await SendAsync(user.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = (Guid?)null, destinationBagId = holidays.Id, amountInCents = 30_000 }, user.CsrfToken);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var savedMovement = api.BagRepository.SavedMovements.Single();
        Assert.Equal(savedMovement.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(30_000, body.GetProperty("amountInCents").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sourceBagId").ValueKind);
        Assert.Equal(holidays.Id, body.GetProperty("destinationBagId").GetGuid());
        Assert.Equal(api.Clock.GetUtcNow().UtcDateTime, body.GetProperty("movedAt").GetDateTime());
    }

    // Chaque refus est donné sur le champ qui ne va pas, et rien n'est déplacé.
    [Fact]
    public async Task MovementRefusalsAreGivenOnTheRightField()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances", 1000);
        var savings = SaveBag(api, user.Account, "Épargne");

        var noBag = await SendAsync(user.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = (Guid?)null, destinationBagId = (Guid?)null, amountInCents = 1 }, user.CsrfToken);
        var sameBag = await SendAsync(user.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = holidays.Id, destinationBagId = holidays.Id, amountInCents = 1 }, user.CsrfToken);
        var amount = await SendAsync(user.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = (Guid?)null, destinationBagId = savings.Id, amountInCents = 0 }, user.CsrfToken);
        var bagTooLow = await SendAsync(user.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = holidays.Id, destinationBagId = savings.Id, amountInCents = 1001 }, user.CsrfToken);
        var availableTooLow = await SendAsync(user.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = (Guid?)null, destinationBagId = savings.Id, amountInCents = 99_001 }, user.CsrfToken);

        Assert.Equal(ErrorMessages.MovementWithoutBag, await ReadFieldErrorAsync(noBag, "DestinationBagId"));
        Assert.Equal(ErrorMessages.MovementWithSameBag, await ReadFieldErrorAsync(sameBag, "DestinationBagId"));
        Assert.Equal(ErrorMessages.MovementAmountBreaksRules, await ReadFieldErrorAsync(amount, "AmountInCents"));
        Assert.Equal(ErrorMessages.SourceBagTooLow, await ReadFieldErrorAsync(bagTooLow, "AmountInCents"));
        Assert.Equal(ErrorMessages.AvailableMoneyTooLow, await ReadFieldErrorAsync(availableTooLow, "AmountInCents"));
        Assert.Single(api.BagRepository.SavedMovements);
    }

    // Les trois champs doivent être écrits, même à null : une requête incomplète ne prend jamais l'argent disponible par erreur.
    [Theory]
    [InlineData("""{ "destinationBagId": "{0}", "amountInCents": 100 }""")]
    [InlineData("""{ "sourceBagId": null, "amountInCents": 100 }""")]
    [InlineData("""{ "sourceBagId": null, "destinationBagId": "{0}" }""")]
    [InlineData("""{ "sourceBagId": null, "destinationBagId": "{0}", "amountInCents": 1.5 }""")]
    [InlineData("""{ "sourceBagId": null, "destinationBagId": "pas-un-identifiant", "amountInCents": 100 }""")]
    public async Task IncompleteOrBadlyWrittenMovementMovesNothing(string jsonPattern)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances");

        var response = await SendRawAsync(user, HttpMethod.Post, MovementsEndpoint, jsonPattern.Replace("{0}", holidays.Id.ToString()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.BagRepository.SavedMovements);
    }

    // SS §6 : au-delà de 1 000 déplacements sur les dernières 24 heures, la réponse est « trop de requêtes ».
    [Fact]
    public async Task MovementBeyondTheDailyLimitIsTooManyRequests()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances");

        for (var number = 1; number <= BagRules.MaximumMovementsPerDay; number++)
        {
            api.BagRepository.SavedMovements.Add(new BagMovement { AmountInCents = 1, MovedAt = api.Clock.GetUtcNow().UtcDateTime, DestinationBagId = holidays.Id });
        }

        var response = await SendAsync(user.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = holidays.Id, destinationBagId = (Guid?)null, amountInCents = 1 }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(BagRules.MaximumMovementsPerDay, api.BagRepository.SavedMovements.Count);
    }

    // --- Historique

    // Ce sont des données financières : aucun navigateur ni intermédiaire ne doit les garder.
    [Fact]
    public async Task HistoryGivesThePageAndIsNeverCached()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances", 40_000);
        var savings = SaveBag(api, user.Account, "Épargne");
        await SendAsync(user.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = holidays.Id, destinationBagId = savings.Id, amountInCents = 15_000 }, user.CsrfToken);

        var response = await user.Client.GetAsync(BagAddress(holidays.Id) + "/movements");
        var body = await ReadJsonAsync(response);
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(BagHistoryService.PageSize, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(-15_000, items[0].GetProperty("amountInCents").GetInt32());
        Assert.Equal(savings.Id, items[0].GetProperty("otherBagId").GetGuid());
        Assert.Equal("Épargne", items[0].GetProperty("otherBagName").GetString());
        Assert.Equal(40_000, items[1].GetProperty("amountInCents").GetInt32());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("otherBagId").ValueKind);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10001")]
    public async Task HistoryPageOutsideTheLimitsIsRefusedOnThePageField(string page)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances");

        var response = await user.Client.GetAsync(BagAddress(holidays.Id) + "/movements?page=" + page);

        Assert.Equal(ErrorMessages.BagHistoryPageBreaksRules, await ReadFieldErrorAsync(response, "Page"));
    }

    [Fact]
    public async Task HistoryPageThatIsNotANumberIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances");

        var response = await user.Client.GetAsync(BagAddress(holidays.Id) + "/movements?page=deux");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApiErrorConfiguration.InvalidValueMessage, await ReadFieldErrorAsync(response, "Page"));
    }

    // --- Requêtes simultanées

    // D'autres requêtes écrivent sans arrêt les mêmes lignes : après 10 essais, chaque écriture répond 409 avec son message,
    // et rien n'est enregistré.
    [Fact]
    public async Task WritesThatKeepLosingAnswerConflict()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var holidays = SaveBag(api, user.Account, "Vacances", 5000);
        var responses = new List<HttpResponseMessage>();

        api.BagRepository.Conflicts = 10;
        responses.Add(await SendAsync(user.Client, HttpMethod.Post, BagsEndpoint, new { name = "Épargne" }, user.CsrfToken));
        api.BagRepository.Conflicts = 10;
        responses.Add(await SendAsync(user.Client, HttpMethod.Put, BagAddress(holidays.Id) + "/name", new { name = "Voyage" }, user.CsrfToken));
        api.BagRepository.Conflicts = 10;
        responses.Add(await SendAsync(user.Client, HttpMethod.Put, BagAddress(holidays.Id) + "/goal", new { targetAmountInCents = 1, targetDate = (DateOnly?)null }, user.CsrfToken));
        api.BagRepository.Conflicts = 10;
        responses.Add(await SendAsync(user.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = holidays.Id, destinationBagId = (Guid?)null, amountInCents = 1 }, user.CsrfToken));
        api.BagRepository.Conflicts = 10;
        responses.Add(await SendAsync(user.Client, HttpMethod.Delete, BagAddress(holidays.Id), null, user.CsrfToken));

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ErrorMessages.BagsChangedMeanwhile, (await ReadJsonAsync(response)).GetProperty("title").GetString());
        }

        Assert.Equal(new List<Bag> { holidays }, api.BagRepository.SavedBags);
        Assert.Equal("Vacances", holidays.Name);
        Assert.Null(holidays.TargetAmountInCents);
        Assert.Single(api.BagRepository.SavedMovements);
    }

    // --- Contrôle d'accès (SS §4)

    // Un utilisateur B ne peut ni lire ni modifier les sacs d'un utilisateur A, ni y prendre ou y mettre de l'argent,
    // et rien ne lui dit qu'ils existent. Chaque tentative est notée au journal de sécurité (SS §11), une par sac de A visé.
    [Fact]
    public async Task UserCannotReadNorChangeTheBagsOfAnotherUser()
    {
        using var api = new ApiFactoryWithFakes();
        var userA = await LogInWithBalanceAsync(api, 100_000);
        var userB = await LogInWithBalanceAsync(api, 100_000, OtherUserEmail);
        var bagOfA = SaveBag(api, userA.Account, "Vacances", 40_000);
        var bagOfB = SaveBag(api, userB.Account, "Épargne", 10_000);

        var listOfB = await ReadJsonAsync(await userB.Client.GetAsync(BagsEndpoint));
        var rename = await SendAsync(userB.Client, HttpMethod.Put, BagAddress(bagOfA.Id) + "/name", new { name = "Piraté" }, userB.CsrfToken);
        var goal = await SendAsync(userB.Client, HttpMethod.Put, BagAddress(bagOfA.Id) + "/goal", new { targetAmountInCents = 1, targetDate = (DateOnly?)null }, userB.CsrfToken);
        var takeFromA = await SendAsync(userB.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = bagOfA.Id, destinationBagId = bagOfB.Id, amountInCents = 40_000 }, userB.CsrfToken);
        var giveToA = await SendAsync(userB.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = bagOfB.Id, destinationBagId = bagOfA.Id, amountInCents = 10_000 }, userB.CsrfToken);
        var withdrawFromA = await SendAsync(userB.Client, HttpMethod.Post, MovementsEndpoint, new { sourceBagId = bagOfA.Id, destinationBagId = (Guid?)null, amountInCents = 40_000 }, userB.CsrfToken);
        var history = await userB.Client.GetAsync(BagAddress(bagOfA.Id) + "/movements");
        var deletion = await SendAsync(userB.Client, HttpMethod.Delete, BagAddress(bagOfA.Id), null, userB.CsrfToken);

        Assert.DoesNotContain(listOfB.GetProperty("bags").EnumerateArray(), bag => bag.GetProperty("id").GetGuid() == bagOfA.Id);
        Assert.Equal(10_000, listOfB.GetProperty("bagsTotalInCents").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, goal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, takeFromA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, giveToA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withdrawFromA.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletion.StatusCode);
        Assert.Equal("Vacances", bagOfA.Name);
        Assert.Null(bagOfA.TargetAmountInCents);
        Assert.Contains(bagOfA, api.BagRepository.SavedBags);
        Assert.Equal(2, api.BagRepository.SavedMovements.Count);
        Assert.Equal(7, CountAccessDenied(api, userB.Account));
        Assert.Equal(0, CountAccessDenied(api, userA.Account));
    }

    // La réponse pour le sac d'un autre est celle d'une adresse qui n'existe pas : elle ne confirme rien.
    [Fact]
    public async Task BagOfAnotherUserAnswersLikeAnUnknownAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var userA = await LogInWithBalanceAsync(api, 100_000);
        var userB = await LogInWithBalanceAsync(api, 100_000, OtherUserEmail);
        var bagOfA = SaveBag(api, userA.Account, "Vacances");

        var foreignBag = await SendAsync(userB.Client, HttpMethod.Delete, BagAddress(bagOfA.Id), null, userB.CsrfToken);
        var unknownBag = await SendAsync(userB.Client, HttpMethod.Delete, BagAddress(Guid.NewGuid()), null, userB.CsrfToken);
        var unknownAddress = await userB.Client.GetAsync("/api/adresse-inconnue");

        // Seul le sac de A laisse une trace au journal : un sac inconnu n'appartient à personne.
        Assert.Equal(1, CountAccessDenied(api, userB.Account));
        var unknownTitle = (await ReadJsonAsync(unknownAddress)).GetProperty("title").GetString();
        Assert.Equal(unknownTitle, (await ReadJsonAsync(foreignBag)).GetProperty("title").GetString());
        Assert.Equal(unknownTitle, (await ReadJsonAsync(unknownBag)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task IdThatIsNotAGuidAnswersNotFound()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);

        var response = await SendAsync(user.Client, HttpMethod.Put, BagsEndpoint + "/1/name", new { name = "Voyage" }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Taille et messages

    // La plus grosse requête valable, le plus long nom écrit en \uXXXX avec un objectif et sa date, tient sous la limite.
    [Fact]
    public async Task LongestValidCreationFitsInTheSizeLimit()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithBalanceAsync(api, 100_000);
        var escapedEmoji = "\\" + "ud83d" + "\\" + "ude00";
        var json = "{\"name\":\"" + string.Concat(Enumerable.Repeat(escapedEmoji, ChosenNameRules.MaximumLength))
            + "\",\"targetAmountInCents\":1000000000,\"targetDate\":\"9999-12-31\"}";

        var response = await SendRawAsync(user, HttpMethod.Post, BagsEndpoint, json);

        Assert.True(Encoding.UTF8.GetByteCount(json) <= RequestSizeConfiguration.MaximumBagRequestBytes);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Les messages écrivent les limites en toutes lettres : ils doivent rester égaux aux règles.
    [Fact]
    public void MessagesGiveTheRealLimits()
    {
        Assert.Contains(BagRules.MaximumCountPerUser + " sacs", ErrorMessages.TooManyBags);
        Assert.Contains("1 à " + ChosenNameRules.MaximumLength + " caractères", ErrorMessages.BagNameBreaksRules);
        Assert.Equal(1_000_000_000, BagRules.MaximumAmountInCents);
        Assert.Contains("10 000 000 €", ErrorMessages.BagGoalAmountBreaksRules);
        Assert.Contains("10 000 000 €", ErrorMessages.MovementAmountBreaksRules);
        Assert.Equal(10_000, BagHistoryService.MaximumPage);
        Assert.Contains("1 à 10 000", ErrorMessages.BagHistoryPageBreaksRules);
    }
}
