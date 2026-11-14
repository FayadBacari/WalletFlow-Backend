using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.BankData;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie les routes des connexions bancaires (GR Comptes, SS §4, §6 et §7) dans la vraie API, avec un faux prestataire
// et une fausse base.
public class BankConnectionEndpointTests
{
    private const string BanksEndpoint = "/api/banks";
    private const string ConnectionsEndpoint = "/api/bank-connections";
    private const string FinishEndpoint = "/api/bank-connections/finish";
    private const string RefreshEndpoint = "/api/bank-connections/refresh";
    private const string OtherUserEmail = "autre-utilisateur@exemple.test";
    private const string VisitorAddress = "198.51.100.23";

    // Un utilisateur connecté : son navigateur, son jeton anti-CSRF et son compte.
    private sealed record LoggedInUser(HttpClient Client, string CsrfToken, User Account);

    // Ajoute un compte, puis le connecte dans un nouveau navigateur, depuis l'adresse IP des tests.
    private static async Task<LoggedInUser> LogInAsync(ApiFactoryWithFakes api, string email = Email)
    {
        var account = await AddAccountAsync(api, email);
        var client = CreateBrowserClient(api);
        client.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, VisitorAddress);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NavigateurDeTest/1.0");
        var csrfToken = await LogInThenGetCsrfTokenAsync(client, email);

        return new LoggedInUser(client, csrfToken, account);
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

    // Commence la connexion de la banque des tests, et renvoie le state que le faux prestataire a reçu.
    private static async Task<string> StartAsync(ApiFactoryWithFakes api, LoggedInUser user)
    {
        var response = await SendAsync(user.Client, HttpMethod.Post, ConnectionsEndpoint, new { bankName = FakeBankDataProvider.BankName }, user.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return api.BankProvider.AuthorizationRequests.Last().State;
    }

    // Connecte la banque des tests avec un compte courant et un compte en dollars, et renvoie l'identifiant de la connexion.
    private static async Task<Guid> ConnectAsync(ApiFactoryWithFakes api, LoggedInUser user)
    {
        var state = await StartAsync(api, user);
        var code = "code-" + Guid.NewGuid().ToString("N");
        api.BankProvider.AddSession(code,
            new TestBankAccount("stable-1", "Compte courant", "EUR", 152_034),
            new TestBankAccount("stable-2", "Compte en dollars", "USD", 100));

        var response = await SendAsync(user.Client, HttpMethod.Post, FinishEndpoint, new { code = code, state = state }, user.CsrfToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await ReadJsonAsync(response)).GetProperty("connectionId").GetGuid();
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

    // Lit la liste des connexions de ce navigateur.
    private static async Task<List<JsonElement>> ListConnectionsAsync(HttpClient client)
    {
        var response = await client.GetAsync(ConnectionsEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await ReadJsonAsync(response)).EnumerateArray().ToList();
    }

    // --- Session et jeton anti-CSRF

    [Fact]
    public async Task EveryBankRouteNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();
        var client = api.CreateClient();
        var anyId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(BanksEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ConnectionsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, ConnectionsEndpoint, new { bankName = "x" }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, FinishEndpoint, new { code = "x", state = "y" }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, RefreshEndpoint, null, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Post, ConnectionsEndpoint + "/" + anyId + "/renew", null, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Delete, ConnectionsEndpoint + "/" + anyId, null, null)).StatusCode);
    }

    // SS §6 : toute requête qui modifie des données demande le jeton anti-CSRF. Sans lui, rien ne se passe chez le prestataire.
    [Fact]
    public async Task WriteRoutesWithoutCsrfTokenDoNothing()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        var connectionId = await ConnectAsync(api, user);
        var requestCount = api.BankProvider.AuthorizationRequests.Count;
        var revokedCount = api.BankProvider.RevokedSessions.Count;

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(user.Client, HttpMethod.Post, ConnectionsEndpoint, new { bankName = FakeBankDataProvider.BankName }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(user.Client, HttpMethod.Post, FinishEndpoint, new { code = "x", state = "y" }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(user.Client, HttpMethod.Post, RefreshEndpoint, null, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(user.Client, HttpMethod.Post, ConnectionsEndpoint + "/" + connectionId + "/renew", null, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(user.Client, HttpMethod.Delete, ConnectionsEndpoint + "/" + connectionId, null, null)).StatusCode);

        Assert.Equal(requestCount, api.BankProvider.AuthorizationRequests.Count);
        Assert.Equal(revokedCount, api.BankProvider.RevokedSessions.Count);
        Assert.Single(api.BankRepository.SavedConnections);
    }

    // --- Banques et début de la connexion

    [Fact]
    public async Task BanksAreListedByName()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await user.Client.GetAsync(BanksEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new List<string> { FakeBankDataProvider.ShortBankName, FakeBankDataProvider.BankName },
            (await ReadJsonAsync(response)).EnumerateArray().Select(bank => bank.GetProperty("name").GetString()!).ToList());
    }

    // Le prestataire ne répond pas : 503, avec un message qui dit de réessayer plus tard.
    [Fact]
    public async Task ProviderDownAnswersServiceUnavailable()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        api.BankProvider.IsDown = true;

        var banks = await user.Client.GetAsync(BanksEndpoint);
        var start = await SendAsync(user.Client, HttpMethod.Post, ConnectionsEndpoint, new { bankName = FakeBankDataProvider.BankName }, user.CsrfToken);

        await AssertProblemTitleAsync(banks, HttpStatusCode.ServiceUnavailable, ErrorMessages.BankServiceUnavailable);
        await AssertProblemTitleAsync(start, HttpStatusCode.ServiceUnavailable, ErrorMessages.BankServiceUnavailable);
    }

    [Fact]
    public async Task StartGivesTheAddressOfTheBank()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await SendAsync(user.Client, HttpMethod.Post, ConnectionsEndpoint, new { bankName = FakeBankDataProvider.BankName }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FakeBankDataProvider.BankAddress, (await ReadJsonAsync(response)).GetProperty("bankAddress").GetString());
        Assert.Equal(TestApiSettings.WebsiteAddress + "/bank-callback", api.BankProvider.AuthorizationRequests.Single().ReturnAddress);
    }

    [Fact]
    public async Task UnknownBankIsRefusedOnTheBankNameField()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);

        var response = await SendAsync(user.Client, HttpMethod.Post, ConnectionsEndpoint, new { bankName = "Banque inventée" }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.BankNotOffered, await ReadFieldErrorAsync(response, "BankName"));
    }

    // --- Retour de la banque et liste

    // GR Comptes : la banque connectée apparaît avec ses comptes en euros, leurs soldes et la date de chaque solde.
    [Fact]
    public async Task ConnectedBankIsListedWithItsAccounts()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        var state = await StartAsync(api, user);
        api.BankProvider.AddSession("code-1",
            new TestBankAccount("stable-1", "Compte courant", "EUR", 152_034),
            new TestBankAccount("stable-2", "Compte en dollars", "USD", 100));

        var finish = await SendAsync(user.Client, HttpMethod.Post, FinishEndpoint, new { code = "code-1", state = state }, user.CsrfToken);
        var listResponse = await user.Client.GetAsync(ConnectionsEndpoint);

        var finishBody = await ReadJsonAsync(finish);
        var connection = Assert.Single((await ReadJsonAsync(listResponse)).EnumerateArray().ToList());
        var account = Assert.Single(connection.GetProperty("accounts").EnumerateArray().ToList());
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        Assert.False(finishBody.GetProperty("isRenewal").GetBoolean());
        Assert.Equal(1, finishBody.GetProperty("addedAccountCount").GetInt32());
        Assert.Equal("Compte en dollars", finishBody.GetProperty("skippedAccountNames")[0].GetString());
        Assert.True(listResponse.Headers.CacheControl?.NoStore);
        Assert.Equal(finishBody.GetProperty("connectionId").GetGuid(), connection.GetProperty("id").GetGuid());
        Assert.Equal(FakeBankDataProvider.BankName, connection.GetProperty("bankName").GetString());
        Assert.Equal("active", connection.GetProperty("status").GetString());
        Assert.False(connection.GetProperty("isEndingSoon").GetBoolean());
        Assert.Equal("Compte courant", account.GetProperty("name").GetString());
        Assert.Equal(152_034, account.GetProperty("balanceInCents").GetInt32());
        Assert.Equal(api.Clock.GetUtcNow().UtcDateTime, account.GetProperty("balanceUpdatedAt").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task WrongStateIsRefusedOnTheStateField()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        api.BankProvider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));

        var response = await SendAsync(user.Client, HttpMethod.Post, FinishEndpoint, new { code = "code-1", state = "state-invente" }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.BankLinkNoLongerValid, await ReadFieldErrorAsync(response, "State"));
        Assert.Equal(0, api.BankProvider.UsedCodeCount);
    }

    [Fact]
    public async Task CodeRefusedByTheBankIsRefusedOnTheCodeField()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        var state = await StartAsync(api, user);

        var response = await SendAsync(user.Client, HttpMethod.Post, FinishEndpoint, new { code = "code-faux", state = state }, user.CsrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorMessages.BankRefusedAuthorization, await ReadFieldErrorAsync(response, "Code"));
    }

    [Fact]
    public async Task BankWithoutEuroAccountIsAConflict()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        var state = await StartAsync(api, user);
        api.BankProvider.AddSession("code-1", new TestBankAccount("stable-1", "Compte en dollars", "USD", 100));

        var response = await SendAsync(user.Client, HttpMethod.Post, FinishEndpoint, new { code = "code-1", state = state }, user.CsrfToken);

        await AssertProblemTitleAsync(response, HttpStatusCode.Conflict, ErrorMessages.NoBankAccountAdded);
    }

    // SS §7 : un lien piégé. B renvoie à WalletFlow le state commencé par A : refusé, et le state ne sert plus à A non plus.
    [Fact]
    public async Task StateStartedByAnotherUserIsRefusedForBoth()
    {
        using var api = new ApiFactoryWithFakes();
        var userA = await LogInAsync(api);
        var userB = await LogInAsync(api, OtherUserEmail);
        var stateOfA = await StartAsync(api, userA);
        api.BankProvider.AddSession("code-de-b", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));

        var responseOfB = await SendAsync(userB.Client, HttpMethod.Post, FinishEndpoint, new { code = "code-de-b", state = stateOfA }, userB.CsrfToken);
        var responseOfA = await SendAsync(userA.Client, HttpMethod.Post, FinishEndpoint, new { code = "code-de-b", state = stateOfA }, userA.CsrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, responseOfB.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, responseOfA.StatusCode);
        Assert.Empty(api.BankRepository.SavedConnections);
        Assert.Equal(0, api.BankProvider.UsedCodeCount);
    }

    // --- Mise à jour

    // GR Comptes et SS §6 : à l'ouverture de l'application, les soldes sont relus, une fois par minute au plus.
    // La banque reçoit l'adresse IP et le navigateur de la requête (DSP2).
    [Fact]
    public async Task RefreshReadsTheNewBalancesOncePerMinute()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        await ConnectAsync(api, user);
        api.BankProvider.ChangeBalance("stable-1", 99_900);
        api.BankProvider.BalanceReadPresences.Clear();

        var first = await SendAsync(user.Client, HttpMethod.Post, RefreshEndpoint, null, user.CsrfToken);
        var second = await SendAsync(user.Client, HttpMethod.Post, RefreshEndpoint, null, user.CsrfToken);

        var account = (await ReadJsonAsync(first))[0].GetProperty("accounts")[0];
        var presence = Assert.Single(api.BankProvider.BalanceReadPresences);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(99_900, account.GetProperty("balanceInCents").GetInt32());
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(IPAddress.Parse(VisitorAddress), presence!.IpAddress);
        Assert.Equal("NavigateurDeTest/1.0", presence.UserAgent);
    }

    // --- Renouvellement et déconnexion

    // GR Comptes : le renouvellement garde la même connexion et les mêmes comptes.
    [Fact]
    public async Task RenewalKeepsTheSameConnection()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        var connectionId = await ConnectAsync(api, user);
        var accountId = (await ListConnectionsAsync(user.Client))[0].GetProperty("accounts")[0].GetProperty("id").GetGuid();

        var renew = await SendAsync(user.Client, HttpMethod.Post, ConnectionsEndpoint + "/" + connectionId + "/renew", null, user.CsrfToken);
        api.BankProvider.AddSession("code-2", new TestBankAccount("stable-1", "Compte courant", "EUR", 7_000));
        var finish = await SendAsync(user.Client, HttpMethod.Post, FinishEndpoint, new { code = "code-2", state = api.BankProvider.AuthorizationRequests.Last().State }, user.CsrfToken);

        var connections = await ListConnectionsAsync(user.Client);
        Assert.Equal(HttpStatusCode.OK, renew.StatusCode);
        Assert.True((await ReadJsonAsync(finish)).GetProperty("isRenewal").GetBoolean());
        var connection = Assert.Single(connections);
        Assert.Equal(connectionId, connection.GetProperty("id").GetGuid());
        Assert.Equal(accountId, connection.GetProperty("accounts")[0].GetProperty("id").GetGuid());
        Assert.Equal(7_000, connection.GetProperty("accounts")[0].GetProperty("balanceInCents").GetInt32());
    }

    // GR Comptes : la déconnexion révoque l'autorisation, puis supprime la banque et ses comptes.
    [Fact]
    public async Task DisconnectionRevokesThenDeletesTheBank()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        var connectionId = await ConnectAsync(api, user);
        var sessionId = api.BankRepository.SavedConnections.Single().ExternalConnectionId;

        var response = await SendAsync(user.Client, HttpMethod.Delete, ConnectionsEndpoint + "/" + connectionId, null, user.CsrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BankConnectionsController.DisconnectedMessage, (await ReadJsonAsync(response)).GetProperty("message").GetString());
        Assert.Contains(sessionId, api.BankProvider.RevokedSessions);
        Assert.Empty(await ListConnectionsAsync(user.Client));
    }

    // Sans la confirmation du prestataire, rien n'est supprimé : 503, l'utilisateur réessaiera.
    [Fact]
    public async Task DisconnectionWithoutRevocationDeletesNothing()
    {
        using var api = new ApiFactoryWithFakes();
        var user = await LogInAsync(api);
        var connectionId = await ConnectAsync(api, user);
        api.BankProvider.RevocationFails = true;

        var response = await SendAsync(user.Client, HttpMethod.Delete, ConnectionsEndpoint + "/" + connectionId, null, user.CsrfToken);

        await AssertProblemTitleAsync(response, HttpStatusCode.ServiceUnavailable, ErrorMessages.BankServiceUnavailable);
        Assert.Single(await ListConnectionsAsync(user.Client));
    }

    // --- Contrôle d'accès (SS §4)

    // SS §4 : un utilisateur B ne peut ni voir, ni renouveler, ni déconnecter les banques d'un utilisateur A.
    [Fact]
    public async Task UserCannotSeeNorChangeTheBanksOfAnotherUser()
    {
        using var api = new ApiFactoryWithFakes();
        var userA = await LogInAsync(api);
        var userB = await LogInAsync(api, OtherUserEmail);
        var connectionOfA = await ConnectAsync(api, userA);
        var requestCount = api.BankProvider.AuthorizationRequests.Count;

        var listOfB = await ListConnectionsAsync(userB.Client);
        var renew = await SendAsync(userB.Client, HttpMethod.Post, ConnectionsEndpoint + "/" + connectionOfA + "/renew", null, userB.CsrfToken);
        var delete = await SendAsync(userB.Client, HttpMethod.Delete, ConnectionsEndpoint + "/" + connectionOfA, null, userB.CsrfToken);

        Assert.Empty(listOfB);
        Assert.Equal(HttpStatusCode.NotFound, renew.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(requestCount, api.BankProvider.AuthorizationRequests.Count);
        Assert.Empty(api.BankProvider.RevokedSessions);
        Assert.Single(await ListConnectionsAsync(userA.Client));
        Assert.Equal(2, api.SecurityEventRepository.SavedSecurityEvents.Count(securityEvent =>
            securityEvent.Type == SecurityEventType.AccessDenied && securityEvent.UserId == userB.Account.Id));
    }

    // --- Démarrage sans prestataire configuré

    // En développement, sans application Enable Banking, l'API démarre quand même, et les routes des banques répondent 503.
    [Fact]
    public async Task DevelopmentWithoutBankSettingsAnswersServiceUnavailable()
    {
        using var api = new ApiFactoryInDevelopmentWithoutBankProvider();
        var user = await LogInAsync(api);

        var response = await user.Client.GetAsync(BanksEndpoint);

        Assert.IsType<BankProviderNotConfigured>(api.Services.GetRequiredService<IBankDataProvider>());
        await AssertProblemTitleAsync(response, HttpStatusCode.ServiceUnavailable, ErrorMessages.BankServiceUnavailable);
    }

    // En production, une clé introuvable fait refuser le démarrage, au lieu d'échouer à la première connexion bancaire.
    [Fact]
    public void ProductionRefusesToStartWithAMissingBankKey()
    {
        using var api = new ApiFactoryWithMissingBankKey();

        var exception = Assert.Throws<InvalidOperationException>(() => api.CreateClient());

        Assert.Contains("EnableBanking:PrivateKeyPath", exception.Message);
    }

    // L'API en développement, sans réglage Enable Banking, et sans le faux prestataire des autres tests.
    private sealed class ApiFactoryInDevelopmentWithoutBankProvider : ApiFactoryInDevelopment
    {
        // Vide les réglages et retire le faux prestataire (nom imposé par le framework).
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseSetting("EnableBanking:ApplicationId", "");
            builder.UseSetting("EnableBanking:PrivateKeyPath", "");
            builder.ConfigureTestServices(services =>
            {
                services.Remove(services.Single(service => service.ImplementationInstance == BankProvider));
            });
        }
    }

    // L'API en production, avec un chemin de clé qui ne mène à aucun fichier.
    private sealed class ApiFactoryWithMissingBankKey : ApiFactoryInProduction
    {
        // Donne un chemin de clé introuvable (nom imposé par le framework).
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseSetting("EnableBanking:PrivateKeyPath", Path.Combine(Path.GetTempPath(), "cle-qui-n-existe-pas-" + Guid.NewGuid() + ".pem"));
        }
    }
}
