using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Api.Filters;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes de l'administration (GR Admin, SS §4, §6 et §8) dans la vraie API, avec une fausse base : elles sont réservées
// à l'administrateur qui a activé la double authentification, et chacune fait ce qu'elle dit.
public class AdministrationEndpointTests
{
    private const string OtherEmail = "autre@exemple.test";
    private const string SearchEndpoint = "/api/admin/accounts/search";

    // Un utilisateur connecté : son navigateur, son jeton anti-CSRF et son compte.
    private sealed record LoggedInUser(HttpClient Client, string CsrfToken, User Account);

    // Une requête d'administration : sa méthode, son adresse et son corps JSON s'il en a un.
    private sealed record AdminRequest(HttpMethod Method, string Address, object? Body);

    // Connecte l'administrateur, avec la double authentification activée.
    private static async Task<LoggedInUser> LogInAdministratorAsync(ApiFactoryWithFakes api)
    {
        var activated = await AddActivatedAccountAsync(api);
        EntityValues.WriteProperty(activated.UserAccount, nameof(User.Role), UserRole.Administrator);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInWithCodeThenGetCsrfTokenAsync(api, client, activated.Secret);

        return new LoggedInUser(client, csrfToken, activated.UserAccount);
    }

    // Connecte un compte avec son mot de passe seul, avec ce rôle.
    private static async Task<LoggedInUser> LogInWithPasswordAsync(ApiFactoryWithFakes api, UserRole role)
    {
        var account = await AddAccountAsync(api, OtherEmail);
        EntityValues.WriteProperty(account, nameof(User.Role), role);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client, OtherEmail);

        return new LoggedInUser(client, csrfToken, account);
    }

    // Toutes les requêtes d'administration, une par route, sur ces identifiants.
    private static List<AdminRequest> CreateOneRequestPerRoute(Guid accountId, Guid requestId)
    {
        return new List<AdminRequest>
        {
            new AdminRequest(HttpMethod.Post, "/api/admin/accounts/search", new { email = OtherEmail }),
            new AdminRequest(HttpMethod.Post, "/api/admin/accounts/" + accountId + "/unlock", null),
            new AdminRequest(HttpMethod.Post, "/api/admin/accounts/" + accountId + "/two-factor-link", null),
            new AdminRequest(HttpMethod.Get, "/api/admin/support-requests?status=sent", null),
            new AdminRequest(HttpMethod.Get, "/api/admin/support-requests/" + requestId, null),
            new AdminRequest(HttpMethod.Post, "/api/admin/support-requests/" + requestId + "/responses", new { content = "Réponse" }),
            new AdminRequest(HttpMethod.Post, "/api/admin/support-requests/" + requestId + "/resolve", null),
            new AdminRequest(HttpMethod.Get, "/api/admin/statistics", null)
        };
    }

    // Envoie cette requête depuis ce navigateur, avec ce jeton anti-CSRF s'il y en a un.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, AdminRequest request, string? csrfToken)
    {
        var message = new HttpRequestMessage(request.Method, request.Address);

        if (request.Body != null)
        {
            message.Content = JsonContent.Create(request.Body);
        }

        if (csrfToken != null)
        {
            message.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);
        }

        return await client.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> SendAsync(LoggedInUser user, HttpMethod method, string address, object? body = null)
    {
        return await SendAsync(user.Client, new AdminRequest(method, address, body), user.CsrfToken);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Garde une demande de ce compte, ou d'un visiteur.
    private static SupportRequest AddRequest(ApiFactoryWithFakes api, User? author, SupportRequestStatus status = SupportRequestStatus.Sent)
    {
        var request = new SupportRequest
        {
            Message = "J'ai perdu mon téléphone.",
            ContactEmail = author == null ? "visiteur@exemple.test" : null,
            Status = status,
            SentAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-1),
            UserId = author?.Id
        };
        api.SupportRepository.SavedRequests.Add(request);

        return request;
    }

    // Compte les accès refusés notés au journal pour ce compte.
    private static int CountAccessDenied(ApiFactoryWithFakes api, User account)
    {
        return api.SecurityEventRepository.SavedSecurityEvents.Count(securityEvent =>
            securityEvent.Type == SecurityEventType.AccessDenied && securityEvent.UserId == account.Id);
    }

    // --- Accès

    // Chaque route sous api/admin porte le filtre qui la réserve à l'administrateur, après le jeton anti-CSRF : une nouvelle route
    // ne peut pas l'oublier.
    [Fact]
    public void EveryAdministrationRouteIsReservedToTheAdministrator()
    {
        using var api = new ApiFactoryWithFakes();
        var routeProvider = api.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var administrationRoutes = routeProvider.ActionDescriptors.Items
            .Where(route => route.AttributeRouteInfo!.Template!.StartsWith("api/admin", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(8, administrationRoutes.Count);
        Assert.All(administrationRoutes, route => Assert.Contains(
            route.FilterDescriptors,
            filter => filter.Filter is ServiceFilterAttribute serviceFilter
                && serviceFilter.ServiceType == typeof(AdministratorOnlyFilter)
                && serviceFilter.Order > new AutoValidateAntiforgeryTokenAttribute().Order));
    }

    [Fact]
    public async Task AdministrationNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();

        var response = await api.CreateClient().GetAsync("/api/admin/statistics");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Un utilisateur qui n'est pas administrateur reçoit 403 sur chaque route, sans rien lire ni changer, et chaque essai est
    // noté au journal comme un accès refusé (SS §11).
    [Fact]
    public async Task UserWhoIsNotAdministratorIsRefusedEverywhereAndJournaled()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithPasswordAsync(api, UserRole.User);
        var request = AddRequest(api, user.Account);
        var requests = CreateOneRequestPerRoute(user.Account.Id, request.Id);

        foreach (var adminRequest in requests)
        {
            var response = await SendAsync(user.Client, adminRequest, user.CsrfToken);

            await AssertProblemTitleAsync(response, HttpStatusCode.Forbidden, ErrorMessages.AdministratorOnly);
        }

        Assert.Equal(requests.Count, CountAccessDenied(api, user.Account));
        Assert.Empty(api.SupportRepository.SavedResponses);
        Assert.Equal(SupportRequestStatus.Sent, request.Status);
        Assert.Empty(api.EmailRequests.SavedRequests);
        Assert.DoesNotContain(api.SecurityEventRepository.SavedSecurityEvents, securityEvent => securityEvent.Type == SecurityEventType.AdminAction);
    }

    // Un administrateur sans double authentification est invité à l'activer : ce n'est pas une tentative d'accès.
    [Fact]
    public async Task AdministratorWithoutTwoFactorIsAskedToActivateIt()
    {
        using var api = new ApiFactoryWithFakes();
        var administrator = await LogInWithPasswordAsync(api, UserRole.Administrator);

        var response = await SendAsync(administrator, HttpMethod.Get, "/api/admin/statistics");

        await AssertProblemTitleAsync(response, HttpStatusCode.Forbidden, ErrorMessages.AdministratorTwoFactorRequired);
        Assert.Equal(0, CountAccessDenied(api, administrator.Account));
    }

    // SS §6 : sans le jeton anti-CSRF, aucune action de l'administrateur ne passe, pas même une recherche, qui écrit au journal.
    [Fact]
    public async Task AdministrationWritesNeedTheCsrfToken()
    {
        using var api = new ApiFactoryWithFakes();
        var administrator = await LogInAdministratorAsync(api);
        var request = AddRequest(api, null);

        var response = await SendAsync(
            administrator.Client,
            new AdminRequest(HttpMethod.Post, "/api/admin/support-requests/" + request.Id + "/responses", new { content = "Réponse" }),
            null);

        var search = await SendAsync(administrator.Client, new AdminRequest(HttpMethod.Post, SearchEndpoint, new { email = OtherEmail }), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.SupportRepository.SavedResponses);
        Assert.Equal(HttpStatusCode.BadRequest, search.StatusCode);
        Assert.DoesNotContain(api.SecurityEventRepository.SavedSecurityEvents, securityEvent => securityEvent.Type == SecurityEventType.AdminAction);
    }

    // Le jeton anti-CSRF est vérifié avant le rôle : une écriture sans lui est refusée sans lire le compte, et rien n'est noté au
    // journal. Un site qui fait envoyer ces écritures par le navigateur d'un utilisateur ne remplit pas son journal d'accès refusés.
    [Fact]
    public async Task CsrfTokenIsCheckedBeforeTheRole()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInWithPasswordAsync(api, UserRole.User);
        var request = AddRequest(api, user.Account);
        var writes = CreateOneRequestPerRoute(user.Account.Id, request.Id).Where(adminRequest => adminRequest.Method == HttpMethod.Post).ToList();

        foreach (var write in writes)
        {
            var response = await SendAsync(user.Client, write, null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(5, writes.Count);
        Assert.Equal(0, CountAccessDenied(api, user.Account));
    }

    // --- Comptes

    // L'administrateur retrouve un compte par son adresse exacte, avec son état et sa double authentification ; une adresse
    // sans compte donne null. Ce sont des données personnelles : rien ne doit les garder en cache.
    [Fact]
    public async Task AdministratorFindsAnAccountByItsExactAddress()
    {
        using var api = new ApiFactoryWithFakes();
        var administrator = await LogInAdministratorAsync(api);
        var account = await AddAccountAsync(api, OtherEmail);

        var found = await SendAsync(administrator, HttpMethod.Post, SearchEndpoint, new { email = "Autre@Exemple.test" });
        var missing = await SendAsync(administrator, HttpMethod.Post, SearchEndpoint, new { email = "autre@exemple.tes" });
        var malformed = await SendAsync(administrator, HttpMethod.Post, SearchEndpoint, new { email = "autre" });

        Assert.True(found.Headers.CacheControl?.NoStore);
        var foundAccount = (await ReadJsonAsync(found)).GetProperty("account");
        Assert.Equal(
            (account.Id, OtherEmail, "active", false),
            (foundAccount.GetProperty("id").GetGuid(), foundAccount.GetProperty("email").GetString(),
                foundAccount.GetProperty("state").GetString(), foundAccount.GetProperty("isTwoFactorEnabled").GetBoolean()));
        Assert.Equal(4, foundAccount.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Null, (await ReadJsonAsync(missing)).GetProperty("account").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.True((await ReadJsonAsync(malformed)).GetProperty("errors").TryGetProperty(nameof(AdminAccountSearchRequest.Email), out _));
    }

    // L'état d'un compte porte son nom du site : non confirmé, actif, bloqué ou en suppression.
    [Theory]
    [InlineData("notConfirmed")]
    [InlineData("active")]
    [InlineData("locked")]
    [InlineData("beingDeleted")]
    public async Task SearchGivesTheStateOfTheAccountByItsName(string stateName)
    {
        using var api = new ApiFactoryWithFakes();
        var administrator = await LogInAdministratorAsync(api);
        var account = await AddAccountAsync(api, OtherEmail);
        var now = api.Clock.GetUtcNow().UtcDateTime;

        switch (stateName)
        {
            case "notConfirmed":
                EntityValues.WriteProperty(account, nameof(User.IsEmailConfirmed), false);
                break;
            case "locked":
                EntityValues.WriteProperty(account, nameof(User.LockoutEndsAt), (DateTime?)now.AddMinutes(15));
                break;
            case "beingDeleted":
                EntityValues.WriteProperty(account, nameof(User.DeletionRequestedAt), (DateTime?)now.AddDays(-1));
                break;
        }

        var found = await SendAsync(administrator, HttpMethod.Post, SearchEndpoint, new { email = OtherEmail });

        Assert.Equal(stateName, (await ReadJsonAsync(found)).GetProperty("account").GetProperty("state").GetString());
    }

    // Le déblocage répond avec le compte débloqué ; un compte qui n'est pas bloqué, ou inconnu, est refusé.
    [Fact]
    public async Task AdministratorUnlocksALockedAccount()
    {
        using var api = new ApiFactoryWithFakes();
        var administrator = await LogInAdministratorAsync(api);
        var account = await AddLockedAccountAsync(api, OtherEmail, 1, api.Clock.GetUtcNow().UtcDateTime.AddMinutes(15));

        var unlocked = await SendAsync(administrator, HttpMethod.Post, "/api/admin/accounts/" + account.Id + "/unlock");
        var again = await SendAsync(administrator, HttpMethod.Post, "/api/admin/accounts/" + account.Id + "/unlock");
        var unknown = await SendAsync(administrator, HttpMethod.Post, "/api/admin/accounts/" + Guid.NewGuid() + "/unlock");

        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        Assert.Equal("active", (await ReadJsonAsync(unlocked)).GetProperty("state").GetString());
        await AssertProblemTitleAsync(again, HttpStatusCode.Conflict, ErrorMessages.AccountNotLocked);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    // Le lien qui désactive la double authentification entre dans la file pour un compte qui l'a activée, et dont le mot de passe
    // n'a pas changé depuis 7 jours ; sinon, il est refusé, et l'administrateur sait pourquoi.
    [Fact]
    public async Task AdministratorSendsTheTwoFactorLinkOnlyToAnAccountThatHasIt()
    {
        using var api = new ApiFactoryWithFakes();
        var administrator = await LogInAdministratorAsync(api);
        var withoutTwoFactor = await AddAccountAsync(api, OtherEmail);
        var withTwoFactor = (await AddActivatedAccountAsync(api, "perdu@exemple.test")).UserAccount;
        var recentlyChanged = (await AddActivatedAccountAsync(api, "recent@exemple.test")).UserAccount;
        api.SecurityEventRepository.SavedSecurityEvents.Add(new SecurityEvent
        {
            Type = SecurityEventType.PasswordChange,
            Result = SecurityEventResult.Success,
            OccurredAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-1),
            UserId = recentlyChanged.Id
        });

        var sent = await SendAsync(administrator, HttpMethod.Post, "/api/admin/accounts/" + withTwoFactor.Id + "/two-factor-link");
        var refused = await SendAsync(administrator, HttpMethod.Post, "/api/admin/accounts/" + withoutTwoFactor.Id + "/two-factor-link");
        var tooRecent = await SendAsync(administrator, HttpMethod.Post, "/api/admin/accounts/" + recentlyChanged.Id + "/two-factor-link");

        Assert.Equal(AdministrationController.TwoFactorLinkSentMessage, (await ReadJsonAsync(sent)).GetProperty("message").GetString());
        await AssertProblemTitleAsync(refused, HttpStatusCode.Conflict, ErrorMessages.AccountTwoFactorNotEnabled);
        await AssertProblemTitleAsync(tooRecent, HttpStatusCode.Conflict, ErrorMessages.AccountPasswordChangedRecently);
        await WaitUntilAsync(() => api.EmailSender.CountSentEmailsTo("perdu@exemple.test") == 1);
        Assert.Contains("/disable-two-factor#token=", api.EmailSender.SentEmails.Single(email => email.RecipientAddress == "perdu@exemple.test").Body);
        Assert.Equal(0, api.EmailSender.CountSentEmailsTo(OtherEmail));
        Assert.Equal(0, api.EmailSender.CountSentEmailsTo("recent@exemple.test"));
    }

    // --- Demandes de support

    // L'administrateur liste les demandes envoyées, en lit une, y répond (201, « en cours », e-mail envoyé), puis la marque
    // résolue ; une seconde fois, elle l'est déjà. La liste et la demande sont des données personnelles : rien ne doit les
    // garder en cache.
    [Fact]
    public async Task AdministratorAnswersThenResolvesARequest()
    {
        using var api = new ApiFactoryWithFakes();
        var administrator = await LogInAdministratorAsync(api);
        var author = await AddAccountAsync(api, OtherEmail);
        var request = AddRequest(api, author);
        AddRequest(api, null, SupportRequestStatus.Resolved);

        var listResponse = await SendAsync(administrator, HttpMethod.Get, "/api/admin/support-requests?status=sent");
        var readResponse = await SendAsync(administrator, HttpMethod.Get, "/api/admin/support-requests/" + request.Id);
        var list = await ReadJsonAsync(listResponse);
        var read = await ReadJsonAsync(readResponse);
        var answered = await SendAsync(administrator, HttpMethod.Post, "/api/admin/support-requests/" + request.Id + "/responses", new { content = "Voici la marche à suivre." });
        var resolved = await SendAsync(administrator, HttpMethod.Post, "/api/admin/support-requests/" + request.Id + "/resolve");
        var again = await SendAsync(administrator, HttpMethod.Post, "/api/admin/support-requests/" + request.Id + "/resolve");

        Assert.True(listResponse.Headers.CacheControl?.NoStore);
        Assert.True(readResponse.Headers.CacheControl?.NoStore);
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal((request.Id, "sent", OtherEmail, author.Id, 0),
            (item.GetProperty("id").GetGuid(), item.GetProperty("status").GetString(), item.GetProperty("replyAddress").GetString(),
                item.GetProperty("accountId").GetGuid(), item.GetProperty("responseCount").GetInt32()));
        Assert.Equal("J'ai perdu mon téléphone.", read.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Created, answered.StatusCode);
        var answeredJson = await ReadJsonAsync(answered);
        Assert.Equal("inProgress", answeredJson.GetProperty("status").GetString());
        Assert.Equal("Voici la marche à suivre.", answeredJson.GetProperty("responses")[0].GetProperty("content").GetString());
        await WaitUntilAsync(() => api.EmailSender.CountSentEmailsTo(OtherEmail) == 1);
        Assert.Contains("Voici la marche à suivre.", api.EmailSender.SentEmails.Single(email => email.RecipientAddress == OtherEmail).Body);
        Assert.Equal("resolved", (await ReadJsonAsync(resolved)).GetProperty("status").GetString());
        await AssertProblemTitleAsync(again, HttpStatusCode.Conflict, ErrorMessages.SupportRequestAlreadyResolved);
    }

    // Une réponse vide est refusée sur son champ ; un état inconnu est refusé ; une demande inconnue n'existe pas.
    [Fact]
    public async Task WrongContentStatusOrRequestIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var administrator = await LogInAdministratorAsync(api);
        var request = AddRequest(api, null);

        var emptyAnswer = await SendAsync(administrator, HttpMethod.Post, "/api/admin/support-requests/" + request.Id + "/responses", new { content = "  " });
        var unknownStatus = await SendAsync(administrator, HttpMethod.Get, "/api/admin/support-requests?status=Sent");
        var unknownRequest = await SendAsync(administrator, HttpMethod.Get, "/api/admin/support-requests/" + Guid.NewGuid());
        var unknownAnswer = await SendAsync(administrator, HttpMethod.Post, "/api/admin/support-requests/" + Guid.NewGuid() + "/responses", new { content = "Réponse" });

        Assert.Equal(HttpStatusCode.BadRequest, emptyAnswer.StatusCode);
        Assert.Equal(ErrorMessages.SupportAnswerBreaksRules, (await ReadJsonAsync(emptyAnswer)).GetProperty("errors").GetProperty("Content")[0].GetString());
        Assert.Equal(ErrorMessages.SupportRequestStatusUnknown, (await ReadJsonAsync(unknownStatus)).GetProperty("errors").GetProperty("status")[0].GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknownRequest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownAnswer.StatusCode);
        Assert.Empty(api.SupportRepository.SavedResponses);
    }

    // --- Statistiques

    // Les comptes par état, les connexions actives et les demandes par état : aucun autre chiffre, aucun montant.
    [Fact]
    public async Task StatisticsGiveOnlyCountsWithoutMoney()
    {
        using var api = new ApiFactoryWithFakes();
        var administrator = await LogInAdministratorAsync(api);
        await AddAccountAsync(api, OtherEmail, isEmailConfirmed: false);
        AddRequest(api, null);

        var response = await SendAsync(administrator, HttpMethod.Get, "/api/admin/statistics");

        Assert.True(response.Headers.CacheControl?.NoStore);
        var statistics = await ReadJsonAsync(response);
        Assert.Equal(new List<string> { "accounts", "activeBankConnections", "supportRequests" }, statistics.EnumerateObject().Select(property => property.Name).ToList());
        Assert.Equal((1, 1, 0, 0),
            (statistics.GetProperty("accounts").GetProperty("notConfirmed").GetInt32(), statistics.GetProperty("accounts").GetProperty("active").GetInt32(),
                statistics.GetProperty("accounts").GetProperty("locked").GetInt32(), statistics.GetProperty("accounts").GetProperty("beingDeleted").GetInt32()));
        Assert.Equal(1, statistics.GetProperty("supportRequests").GetProperty("sent").GetInt32());
    }
}
