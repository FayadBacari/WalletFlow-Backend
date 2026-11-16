using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes du support (GR Admin, SS §4 à §6) dans la vraie API, avec une fausse base : l'utilisateur connecté écrit,
// suit ses demandes et lit les réponses ; un visiteur écrit sans session, dans la limite de 3 demandes par heure et par adresse IP.
public class SupportRequestEndpointTests
{
    private const string RequestsEndpoint = "/api/support-requests";
    private const string VisitorEndpoint = RequestsEndpoint + "/visitor";

    // Un utilisateur connecté : son navigateur, son jeton anti-CSRF et son compte.
    private sealed record LoggedInUser(HttpClient Client, string CsrfToken, User Account);

    private static async Task<LoggedInUser> LogInAsync(ApiFactoryWithFakes api)
    {
        var account = await AddAccountAsync(api, Email);
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client, Email);

        return new LoggedInUser(client, csrfToken, account);
    }

    // Envoie ce corps en POST, avec ce jeton anti-CSRF si le test en donne.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string address, object body, string? csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(body) };

        if (csrfToken != null)
        {
            request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);
        }

        return await client.SendAsync(request);
    }

    // Écrit au support au nom de l'utilisateur connecté.
    private static async Task<HttpResponseMessage> SendAsync(LoggedInUser user, string? message)
    {
        return await PostAsync(user.Client, RequestsEndpoint, new { message = message }, user.CsrfToken);
    }

    // Crée un navigateur sans session, qui écrit depuis cette adresse IP.
    private static HttpClient CreateVisitorClient(ApiFactoryWithFakes api, string ipAddress)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, ipAddress);

        return client;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Lit le message d'erreur de ce champ dans une réponse 400.
    private static async Task<string?> ReadFieldErrorAsync(HttpResponseMessage response, string fieldName)
    {
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");

        return errors.TryGetProperty(fieldName, out var fieldErrors) ? fieldErrors[0].GetString() : null;
    }

    // --- Utilisateur connecté

    [Fact]
    public async Task UserRoutesNeedASession()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(RequestsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(RequestsEndpoint + "/" + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, RequestsEndpoint, new { message = "Bonjour" }, null)).StatusCode);
        Assert.Empty(api.SupportRepository.SavedRequests);
    }

    // SS §6 : sans le jeton anti-CSRF, rien n'est envoyé.
    [Fact]
    public async Task SendingWithoutCsrfTokenSavesNothing()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await PostAsync(user.Client, RequestsEndpoint, new { message = "Bonjour" }, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.SupportRepository.SavedRequests);
    }

    // La demande envoyée revient telle qu'elle est gardée, puis se retrouve dans la liste et se relit à l'identique.
    // Ce sont des données personnelles : aucun navigateur ni intermédiaire ne doit les garder.
    [Fact]
    public async Task SentRequestIsListedThenReadAsItWasReturned()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var sent = await SendAsync(user, "  Bonjour,\r\nmon sac a disparu.  ");
        var sentText = await sent.Content.ReadAsStringAsync();
        var list = await user.Client.GetAsync(RequestsEndpoint);
        var saved = Assert.Single(api.SupportRepository.SavedRequests);
        var read = await user.Client.GetAsync(RequestsEndpoint + "/" + saved.Id);

        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        var created = await ReadJsonAsync(sent);
        Assert.Equal(saved.Id, created.GetProperty("id").GetGuid());
        Assert.Equal("sent", created.GetProperty("status").GetString());
        Assert.Equal("Bonjour,\nmon sac a disparu.", created.GetProperty("message").GetString());
        Assert.Equal(0, created.GetProperty("responses").GetArrayLength());
        Assert.Equal(api.Clock.GetUtcNow().UtcDateTime, created.GetProperty("sentAt").GetDateTime());
        Assert.Equal(user.Account.Id, saved.UserId);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl?.NoStore);
        var item = Assert.Single((await ReadJsonAsync(list)).GetProperty("items").EnumerateArray());
        Assert.Equal((saved.Id, "sent", 0), (item.GetProperty("id").GetGuid(), item.GetProperty("status").GetString(), item.GetProperty("responseCount").GetInt32()));
        Assert.False(item.TryGetProperty("message", out _));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
        Assert.Equal(sentText, await read.Content.ReadAsStringAsync());
    }

    // Les réponses se lisent de la plus ancienne à la plus récente, et chaque état a son nom pour le site.
    [Theory]
    [InlineData(SupportRequestStatus.Sent, "sent")]
    [InlineData(SupportRequestStatus.InProgress, "inProgress")]
    [InlineData(SupportRequestStatus.Resolved, "resolved")]
    public async Task ResponsesAreReadInOrderWithTheStatusName(SupportRequestStatus status, string statusName)
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        var now = api.Clock.GetUtcNow().UtcDateTime;
        var request = new SupportRequest { Message = "Bonjour", Status = status, SentAt = now.AddDays(-2), UserId = user.Account.Id };
        api.SupportRepository.SavedRequests.Add(request);
        api.SupportRepository.SavedResponses.Add(new SupportResponse { Content = "Seconde", SentAt = now, SupportRequestId = request.Id });
        api.SupportRepository.SavedResponses.Add(new SupportResponse { Content = "Première", SentAt = now.AddDays(-1), SupportRequestId = request.Id });

        var read = await ReadJsonAsync(await user.Client.GetAsync(RequestsEndpoint + "/" + request.Id));
        var item = Assert.Single((await ReadJsonAsync(await user.Client.GetAsync(RequestsEndpoint))).GetProperty("items").EnumerateArray());

        Assert.Equal(statusName, read.GetProperty("status").GetString());
        Assert.Equal(
            new List<string?> { "Première", "Seconde" },
            read.GetProperty("responses").EnumerateArray().Select(response => response.GetProperty("content").GetString()).ToList());
        Assert.Equal((statusName, 2), (item.GetProperty("status").GetString(), item.GetProperty("responseCount").GetInt32()));
    }

    // Un message vide ou qui contient un caractère invisible est refusé sur son champ, sans rien enregistrer.
    [Fact]
    public async Task MessageThatBreaksTheRulesIsRefusedOnItsField()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var empty = await SendAsync(user, "  ");
        var invisible = await SendAsync(user, "a" + (char)0x202E + "b");
        var missing = await PostAsync(user.Client, RequestsEndpoint, new { }, user.CsrfToken);

        foreach (var response in new[] { empty, invisible, missing })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ErrorMessages.SupportMessageBreaksRules, await ReadFieldErrorAsync(response, nameof(SupportMessageRequest.Message)));
        }

        Assert.Empty(api.SupportRepository.SavedRequests);
    }

    // SS §6 : la 11e demande des dernières 24 heures reçoit « trop de requêtes », comme toute limite de débit, et n'est pas
    // enregistrée.
    [Fact]
    public async Task EleventhRequestWithinTwentyFourHoursIsRefusedWithTooManyRequests()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        for (var number = 1; number <= 10; number++)
        {
            Assert.Equal(HttpStatusCode.Created, (await SendAsync(user, "Demande " + number)).StatusCode);
        }

        var eleventh = await SendAsync(user, "Demande 11");

        await AssertProblemTitleAsync(eleventh, HttpStatusCode.TooManyRequests, "Trop de demandes. Réessaie plus tard.");
        Assert.Equal(10, api.SupportRepository.SavedRequests.Count);
    }

    // Une demande qui n'existe pas répond « introuvable ».
    [Fact]
    public async Task UnknownRequestIsNotFound()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await user.Client.GetAsync(RequestsEndpoint + "/" + Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Visiteur

    // Un visiteur écrit sans session ni jeton anti-CSRF. L'adresse est gardée en minuscules, et aucun e-mail ne part.
    [Fact]
    public async Task VisitorWritesWithoutSessionAndReceivesNoEmail()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateVisitorClient(api, "192.0.2.10");

        var response = await PostAsync(client, VisitorEndpoint, new { contactEmail = " Visiteur@Exemple.TEST ", message = "Mon compte est bloqué." }, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SupportRequestsController.VisitorRequestSentMessage, (await ReadJsonAsync(response)).GetProperty("message").GetString());
        var saved = Assert.Single(api.SupportRepository.SavedRequests);
        Assert.Equal(((Guid?)null, "visiteur@exemple.test", "Mon compte est bloqué."), (saved.UserId, saved.ContactEmail, saved.Message));
        Assert.Empty(api.EmailRequests.SavedRequests);
        Assert.Empty(api.EmailSender.SentEmails);
    }

    // Les deux champs refusés sont signalés en une seule réponse.
    [Fact]
    public async Task VisitorFieldsThatBreakTheRulesAreReportedTogether()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateVisitorClient(api, "192.0.2.11");

        var response = await PostAsync(client, VisitorEndpoint, new { contactEmail = "pas-une-adresse", message = "a\tb" }, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.InvalidEmailAddress, await ReadFieldErrorAsync(response, nameof(VisitorSupportMessageRequest.ContactEmail)));
        Assert.Equal(ErrorMessages.SupportMessageBreaksRules, await ReadFieldErrorAsync(response, nameof(VisitorSupportMessageRequest.Message)));
        Assert.Empty(api.SupportRepository.SavedRequests);
    }

    // SS §6 : 3 demandes par heure et par adresse IP. Une autre adresse a son propre compteur.
    [Fact]
    public async Task FourthVisitorRequestFromTheSameAddressIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var visitor = CreateVisitorClient(api, "192.0.2.12");
        var otherVisitor = CreateVisitorClient(api, "192.0.2.13");
        var body = new { contactEmail = "visiteur@exemple.test", message = "Bonjour" };

        for (var number = 1; number <= 3; number++)
        {
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(visitor, VisitorEndpoint, body, null)).StatusCode);
        }

        var fourth = await PostAsync(visitor, VisitorEndpoint, body, null);
        var fromAnotherAddress = await PostAsync(otherVisitor, VisitorEndpoint, body, null);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fromAnotherAddress.StatusCode);
        Assert.Equal(4, api.SupportRepository.SavedRequests.Count);
    }

    // SS §6 : la route n'accepte que du JSON, qu'un site piégé ne peut pas envoyer sans autorisation.
    [Fact]
    public async Task VisitorRouteAcceptsOnlyJson()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateVisitorClient(api, "192.0.2.14");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "contactEmail", "visiteur@exemple.test" },
            { "message", "Bonjour" }
        });

        var response = await client.PostAsync(VisitorEndpoint, form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(api.SupportRepository.SavedRequests);
    }

    // La route des visiteurs ne lit que ces deux champs : un identifiant d'utilisateur envoyé en plus ne rattache la demande
    // à aucun compte (SS §4).
    [Fact]
    public async Task VisitorRequestIgnoresAnyOtherField()
    {
        using var api = new ApiFactoryWithFakes();
        var account = await AddAccountAsync(api, Email);
        var client = CreateVisitorClient(api, "192.0.2.15");
        var json = "{\"contactEmail\":\"visiteur@exemple.test\",\"message\":\"Bonjour\",\"userId\":\"" + account.Id + "\",\"status\":\"resolved\"}";

        var response = await client.PostAsync(VisitorEndpoint, new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = Assert.Single(api.SupportRepository.SavedRequests);
        Assert.Equal(((Guid?)null, SupportRequestStatus.Sent), (saved.UserId, saved.Status));
    }
}
