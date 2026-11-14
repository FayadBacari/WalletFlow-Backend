using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes des catégories (GR Dépenses, SS §4, §5 et §6) dans la vraie API, avec une fausse base.
public class CategoryEndpointTests
{
    private const string CategoriesEndpoint = "/api/categories";
    private const string OtherUserEmail = "autre-utilisateur@exemple.test";

    // Un utilisateur connecté : son navigateur, son jeton anti-CSRF et son compte.
    private sealed record LoggedInUser(HttpClient Client, string CsrfToken, User Account);

    // Ajoute un compte avec ses 8 catégories par défaut, puis le connecte dans un nouveau navigateur.
    private static async Task<LoggedInUser> LogInWithCategoriesAsync(ApiFactoryWithFakes api, string email = Email)
    {
        var account = await AddAccountAsync(api, email);
        api.CategoryRepository.SavedCategories.AddRange(DefaultCategories.CreateForUser(account.Id));
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client, email);

        return new LoggedInUser(client, csrfToken, account);
    }

    // Donne la catégorie de ce compte qui porte ce nom.
    private static Category FindCategory(ApiFactoryWithFakes api, User account, string name)
    {
        return api.CategoryRepository.SavedCategories.Single(category => category.UserId == account.Id && category.Name == name);
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

    // Envoie ce texte JSON tel quel à la route du budget : certains montants ne s'écrivent pas avec un objet C#.
    private static async Task<HttpResponseMessage> SendRawBudgetAsync(LoggedInUser user, Guid categoryId, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, BudgetAddress(categoryId))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, user.CsrfToken);

        return await user.Client.SendAsync(request);
    }

    // Adresse du nom de cette catégorie.
    private static string NameAddress(Guid categoryId)
    {
        return CategoriesEndpoint + "/" + categoryId + "/name";
    }

    // Adresse du budget de cette catégorie.
    private static string BudgetAddress(Guid categoryId)
    {
        return CategoriesEndpoint + "/" + categoryId + "/budget";
    }

    // Adresse de cette catégorie.
    private static string CategoryAddress(Guid categoryId)
    {
        return CategoriesEndpoint + "/" + categoryId;
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
    public async Task EveryCategoryRouteNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        var anyId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(CategoriesEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, CategoriesEndpoint, new { name = "Abonnements" }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Put, NameAddress(anyId), new { name = "Sorties" }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Put, BudgetAddress(anyId), new { monthlyBudgetInCents = 3000 }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Delete, CategoryAddress(anyId), null, null)).StatusCode);
    }

    // SS §6 : toute requête qui modifie des données demande le jeton anti-CSRF. Sans lui, rien ne change.
    [Fact]
    public async Task WriteRoutesWithoutCsrfTokenChangeNothing()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        var leisure = FindCategory(api, user.Account, "Loisirs");

        var creation = await SendAsync(user.Client, HttpMethod.Post, CategoriesEndpoint, new { name = "Abonnements" }, null);
        var rename = await SendAsync(user.Client, HttpMethod.Put, NameAddress(leisure.Id), new { name = "Sorties" }, null);
        var budget = await SendAsync(user.Client, HttpMethod.Put, BudgetAddress(leisure.Id), new { monthlyBudgetInCents = 3000 }, null);
        var deletion = await SendAsync(user.Client, HttpMethod.Delete, CategoryAddress(leisure.Id), null, null);

        Assert.Equal(HttpStatusCode.BadRequest, creation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, budget.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, deletion.StatusCode);
        Assert.Equal(8, api.CategoryRepository.SavedCategories.Count);
        Assert.Equal("Loisirs", leisure.Name);
        Assert.Null(leisure.MonthlyBudgetInCents);
    }

    // --- Liste

    // Ce sont des données financières : aucun navigateur ni intermédiaire ne doit les garder.
    [Fact]
    public async Task ListGivesTheDefaultCategoriesInOrderAndIsNeverCached()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);

        var response = await user.Client.GetAsync(CategoriesEndpoint);
        var categories = (await ReadJsonAsync(response)).EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            new List<string> { "Alimentation", "Logement", "Loisirs", "Revenus", "Santé", "Shopping", "Transport", "Autres" },
            categories.Select(category => category.GetProperty("name").GetString()!).ToList());
        Assert.True(categories.Last().GetProperty("isOtherCategory").GetBoolean());
        Assert.False(categories.First().GetProperty("isOtherCategory").GetBoolean());
        Assert.Equal(JsonValueKind.Null, categories.First().GetProperty("monthlyBudgetInCents").ValueKind);
        Assert.Equal(FindCategory(api, user.Account, "Alimentation").Id, categories.First().GetProperty("id").GetGuid());
    }

    // --- Création

    [Fact]
    public async Task CreationAnswersCreatedWithTheNewCategory()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);

        var response = await SendAsync(user.Client, HttpMethod.Post, CategoriesEndpoint, new { name = "  Abonnements " }, user.CsrfToken);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Abonnements", body.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("monthlyBudgetInCents").ValueKind);
        Assert.False(body.GetProperty("isOtherCategory").GetBoolean());
        Assert.Equal(FindCategory(api, user.Account, "Abonnements").Id, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task InvalidNameIsRefusedOnTheNameField()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);

        var response = await SendAsync(user.Client, HttpMethod.Post, CategoriesEndpoint, new { name = "   " }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.CategoryNameBreaksRules, await ReadFieldErrorAsync(response, "Name"));
        Assert.Equal(8, api.CategoryRepository.SavedCategories.Count);
    }

    [Fact]
    public async Task NameAlreadyUsedIsRefusedOnTheNameField()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);

        var response = await SendAsync(user.Client, HttpMethod.Post, CategoriesEndpoint, new { name = "LOISIRS" }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.CategoryNameAlreadyUsed, await ReadFieldErrorAsync(response, "Name"));
    }

    // SS §6 : 100 catégories au plus.
    [Fact]
    public async Task HundredAndFirstCategoryIsRefusedWithAConflict()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        for (var number = 1; number <= 92; number++)
        {
            api.CategoryRepository.SavedCategories.Add(new Category { Name = "Catégorie " + number, UserId = user.Account.Id });
        }

        var response = await SendAsync(user.Client, HttpMethod.Post, CategoriesEndpoint, new { name = "Une de trop" }, user.CsrfToken);

        await AssertProblemTitleAsync(response, HttpStatusCode.Conflict, ErrorMessages.TooManyCategories);
        Assert.Equal(100, api.CategoryRepository.SavedCategories.Count);
    }

    // SS §4 : seuls les champs prévus sont lus. Une requête ne peut ni créer une seconde « Autres »,
    // ni écrire chez un autre utilisateur, ni donner un budget à la création.
    [Fact]
    public async Task FieldsAddedToTheCreationAreIgnored()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        var otherUserId = Guid.NewGuid();

        var response = await SendAsync(
            user.Client,
            HttpMethod.Post,
            CategoriesEndpoint,
            new { name = "Abonnements", isOtherCategory = true, userId = otherUserId, monthlyBudgetInCents = 5, id = Guid.Empty },
            user.CsrfToken);

        var created = FindCategory(api, user.Account, "Abonnements");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(created.IsOtherCategory);
        Assert.Null(created.MonthlyBudgetInCents);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.DoesNotContain(api.CategoryRepository.SavedCategories, category => category.UserId == otherUserId);
    }

    // --- Nom

    [Fact]
    public async Task RenameAnswersTheRenamedCategory()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        var leisure = FindCategory(api, user.Account, "Loisirs");

        var response = await SendAsync(user.Client, HttpMethod.Put, NameAddress(leisure.Id), new { name = "Sorties" }, user.CsrfToken);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Sorties", body.GetProperty("name").GetString());
        Assert.Equal(leisure.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Sorties", leisure.Name);
    }

    // GR Dépenses : « Autres » ne peut être ni renommée ni supprimée.
    [Fact]
    public async Task OtherCategoryIsNeitherRenamedNorDeleted()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        var otherCategory = FindCategory(api, user.Account, "Autres");

        var rename = await SendAsync(user.Client, HttpMethod.Put, NameAddress(otherCategory.Id), new { name = "Divers" }, user.CsrfToken);
        var deletion = await SendAsync(user.Client, HttpMethod.Delete, CategoryAddress(otherCategory.Id), null, user.CsrfToken);

        await AssertProblemTitleAsync(rename, HttpStatusCode.Conflict, ErrorMessages.OtherCategoryIsFixed);
        await AssertProblemTitleAsync(deletion, HttpStatusCode.Conflict, ErrorMessages.OtherCategoryIsFixed);
        Assert.Contains(otherCategory, api.CategoryRepository.SavedCategories);
        Assert.Equal("Autres", otherCategory.Name);
    }

    // --- Budget

    [Fact]
    public async Task BudgetIsSetThenRemoved()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        var leisure = FindCategory(api, user.Account, "Loisirs");

        var set = await SendAsync(user.Client, HttpMethod.Put, BudgetAddress(leisure.Id), new { monthlyBudgetInCents = 3000 }, user.CsrfToken);
        Assert.Equal(3000, (await ReadJsonAsync(set)).GetProperty("monthlyBudgetInCents").GetInt32());

        var removed = await SendRawBudgetAsync(user, leisure.Id, """{ "monthlyBudgetInCents": null }""");
        Assert.Equal(JsonValueKind.Null, (await ReadJsonAsync(removed)).GetProperty("monthlyBudgetInCents").ValueKind);

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Null(leisure.MonthlyBudgetInCents);
    }

    // Le champ doit être écrit, même à null : une requête vide n'enlève jamais un budget par erreur.
    [Fact]
    public async Task EmptyBudgetRequestKeepsTheBudget()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        var leisure = FindCategory(api, user.Account, "Loisirs");
        await SendRawBudgetAsync(user, leisure.Id, """{ "monthlyBudgetInCents": 3000 }""");

        var response = await SendRawBudgetAsync(user, leisure.Id, "{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(3000, leisure.MonthlyBudgetInCents);
    }

    // SS §5 : un montant est un nombre entier de centimes. Aucun nombre à virgule, aucun texte, aucun dépassement.
    [Theory]
    [InlineData("""{ "monthlyBudgetInCents": 12.5 }""")]
    [InlineData("""{ "monthlyBudgetInCents": "3000" }""")]
    [InlineData("""{ "monthlyBudgetInCents": 3000000000 }""")]
    [InlineData("""{ "monthlyBudgetInCents": true }""")]
    [InlineData("""{ "monthlyBudgetInCents": 1e3 }""")]
    public async Task BudgetThatIsNotAWholeNumberOfCentsIsRefused(string json)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        var leisure = FindCategory(api, user.Account, "Loisirs");

        var response = await SendRawBudgetAsync(user, leisure.Id, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(leisure.MonthlyBudgetInCents);
    }

    [Fact]
    public async Task BudgetOutsideTheLimitsIsRefusedOnTheBudgetField()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        var leisure = FindCategory(api, user.Account, "Loisirs");

        var response = await SendRawBudgetAsync(user, leisure.Id, """{ "monthlyBudgetInCents": 0 }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.CategoryBudgetBreaksRules, await ReadFieldErrorAsync(response, "MonthlyBudgetInCents"));
        Assert.Null(leisure.MonthlyBudgetInCents);
    }

    // --- Suppression

    [Fact]
    public async Task DeletionAnswersAMessageAndTheCategoryLeavesTheList()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        var leisure = FindCategory(api, user.Account, "Loisirs");

        var response = await SendAsync(user.Client, HttpMethod.Delete, CategoryAddress(leisure.Id), null, user.CsrfToken);
        var list = await ReadJsonAsync(await user.Client.GetAsync(CategoriesEndpoint));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CategoriesController.CategoryDeletedMessage, (await ReadJsonAsync(response)).GetProperty("message").GetString());
        Assert.Equal(7, list.GetArrayLength());
        Assert.DoesNotContain(list.EnumerateArray(), category => category.GetProperty("id").GetGuid() == leisure.Id);
    }

    // --- Contrôle d'accès (SS §4)

    // SS §4 : un utilisateur B ne peut ni lire ni modifier les catégories d'un utilisateur A, et rien ne lui dit qu'elles existent.
    [Fact]
    public async Task UserCannotReadNorChangeTheCategoriesOfAnotherUser()
    {
        using var api = new ApiFactoryWithFakes();
        var userA = await LogInWithCategoriesAsync(api);
        var userB = await LogInWithCategoriesAsync(api, OtherUserEmail);
        var leisureOfA = FindCategory(api, userA.Account, "Loisirs");

        var listOfB = await ReadJsonAsync(await userB.Client.GetAsync(CategoriesEndpoint));
        var rename = await SendAsync(userB.Client, HttpMethod.Put, NameAddress(leisureOfA.Id), new { name = "Piratée" }, userB.CsrfToken);
        var budget = await SendAsync(userB.Client, HttpMethod.Put, BudgetAddress(leisureOfA.Id), new { monthlyBudgetInCents = 1 }, userB.CsrfToken);
        var deletion = await SendAsync(userB.Client, HttpMethod.Delete, CategoryAddress(leisureOfA.Id), null, userB.CsrfToken);

        Assert.DoesNotContain(listOfB.EnumerateArray(), category => category.GetProperty("id").GetGuid() == leisureOfA.Id);
        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, budget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletion.StatusCode);
        Assert.Equal("Loisirs", leisureOfA.Name);
        Assert.Null(leisureOfA.MonthlyBudgetInCents);
        Assert.Contains(leisureOfA, api.CategoryRepository.SavedCategories);
        Assert.Equal(3, CountAccessDenied(api, userB.Account));
        Assert.Equal(0, CountAccessDenied(api, userA.Account));
    }

    // La réponse pour la catégorie d'un autre est celle d'une adresse qui n'existe pas : elle ne confirme rien.
    [Fact]
    public async Task CategoryOfAnotherUserAnswersLikeAnUnknownAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var userA = await LogInWithCategoriesAsync(api);
        var userB = await LogInWithCategoriesAsync(api, OtherUserEmail);
        var leisureOfA = FindCategory(api, userA.Account, "Loisirs");

        var foreignCategory = await SendAsync(userB.Client, HttpMethod.Delete, CategoryAddress(leisureOfA.Id), null, userB.CsrfToken);
        var unknownCategory = await SendAsync(userB.Client, HttpMethod.Delete, CategoryAddress(Guid.NewGuid()), null, userB.CsrfToken);
        var unknownAddress = await userB.Client.GetAsync("/api/adresse-inconnue");

        var unknownTitle = (await ReadJsonAsync(unknownAddress)).GetProperty("title").GetString();
        Assert.Equal(unknownTitle, (await ReadJsonAsync(foreignCategory)).GetProperty("title").GetString());
        Assert.Equal(unknownTitle, (await ReadJsonAsync(unknownCategory)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task IdThatIsNotAGuidAnswersNotFound()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);

        var response = await SendAsync(user.Client, HttpMethod.Put, CategoriesEndpoint + "/1/name", new { name = "Sorties" }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Taille et messages

    // Le serveur de test n'applique pas la limite de taille : Kestrel le fait, comme le prouve RegistrationRequestSizeLimitTests,
    // et l'inventaire des routes vérifie que chaque route des catégories la déclare. Ici, on vérifie que la plus grosse requête
    // valable, le plus long nom écrit en \uXXXX, tient sous la limite et reste acceptée.
    [Fact]
    public async Task LongestValidNameFitsInTheSizeLimit()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithCategoriesAsync(api);
        var escapedEmoji = "\\" + "ud83d" + "\\" + "ude00";
        var json = "{\"name\":\"" + string.Concat(Enumerable.Repeat(escapedEmoji, CategoryRules.MaximumNameLength)) + "\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, CategoriesEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, user.CsrfToken);

        var response = await user.Client.SendAsync(request);

        Assert.True(Encoding.UTF8.GetByteCount(json) <= RequestSizeConfiguration.MaximumCategoryRequestBytes);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Les messages écrivent les limites en toutes lettres : ils doivent rester égaux aux règles.
    [Fact]
    public void MessagesGiveTheRealLimits()
    {
        Assert.Contains(CategoryRules.MaximumCountPerUser + " catégories", ErrorMessages.TooManyCategories);
        Assert.Contains("1 à " + CategoryRules.MaximumNameLength + " caractères", ErrorMessages.CategoryNameBreaksRules);
        Assert.Equal(1_000_000_000, CategoryRules.MaximumMonthlyBudgetInCents);
        Assert.Contains("10 000 000 €", ErrorMessages.CategoryBudgetBreaksRules);
    }
}
