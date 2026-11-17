using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Application.DataExport;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la route qui donne le fichier des données (GR Compte, SS §2, §6 et §11) dans la vraie API, avec une fausse base :
// le mot de passe, la session et le jeton anti-CSRF, 3 essais par heure, et un fichier JSON téléchargé, jamais gardé en cache,
// où chaque valeur porte son nom du site.
public class DataExportEndpointTests
{
    private const string ExportEndpoint = "/api/account/export";
    private static readonly DateTime Moment = new DateTime(2026, 11, 15, 8, 30, 0, DateTimeKind.Utc);

    // Des données avec une valeur de chaque liste, pour lire leurs noms dans le fichier.
    private static AccountData CreateData()
    {
        var connectionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var bagId = Guid.NewGuid();
        var suggestionId = Guid.NewGuid();

        return new AccountData(
            new ExportedProfile(Email, "Durand", "Alice", false, NotificationChannel.EmailAndPhone),
            new List<ExportedBankConnection> { new ExportedBankConnection(connectionId, "Banque", BankConnectionStatus.Expired, Moment) },
            new List<ExportedBankAccount> { new ExportedBankAccount(accountId, connectionId, "Compte courant", 12_345, Moment) },
            new List<ExportedTransaction> { new ExportedTransaction(Guid.NewGuid(), accountId, new DateOnly(2026, 11, 3), "CB CARREFOUR", -1250, categoryId, false) },
            new List<ExportedCategory> { new ExportedCategory(categoryId, "Courses", 30_000, false) },
            new List<ExportedCategorizationRule> { new ExportedCategorizationRule("CB CARREFOUR", categoryId) },
            new List<ExportedBag> { new ExportedBag(bagId, "Vacances", 100_000, new DateOnly(2027, 7, 1)) },
            new List<ExportedBagMovement> { new ExportedBagMovement(Guid.NewGuid(), Moment, 3000, null, bagId, suggestionId) },
            new List<ExportedSuggestion>
            {
                new ExportedSuggestion(suggestionId, new DateOnly(2026, 10, 1), SuggestionRule.CategoryOverrun, categoryId, 2500, SuggestionStatus.Ignored, Moment)
            },
            new List<ExportedAlertSetting> { new ExportedAlertSetting(AlertKind.BudgetReached, false) },
            new List<ExportedAlert> { new ExportedAlert(Guid.NewGuid(), AlertKind.BagGoalReached, Moment, null, null, null, bagId, null) },
            new List<ExportedSupportRequest>
            {
                new ExportedSupportRequest(Guid.NewGuid(), Moment, SupportRequestStatus.InProgress, "Bonjour", new List<ExportedSupportResponse> { new ExportedSupportResponse(Moment, "Réponse") })
            });
    }

    // Connecte un compte sans double authentification, avec des données à télécharger. Renvoie son navigateur et son jeton anti-CSRF.
    private static async Task<(HttpClient Client, string CsrfToken, User Account)> LogInWithDataAsync(ApiFactoryWithFakes api)
    {
        var account = await AddAccountAsync(api);
        api.ExportRepository.SavedData[account.Id] = CreateData();
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInThenGetCsrfTokenAsync(client);

        return (client, csrfToken, account);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Le bon mot de passe donne le fichier JSON, à enregistrer sous le nom du jour à Paris, sans cache. Chaque liste y est, et
    // chaque valeur porte son nom du site. Le téléchargement est noté au journal.
    [Fact]
    public async Task RightPasswordDownloadsTheFile()
    {
        using var api = new ApiFactoryWithFakes();
        var (client, csrfToken, account) = await LogInWithDataAsync(api);

        var response = await SendSettingsRequestAsync(client, ExportEndpoint, new { password = ValidPassword }, csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var day = UsersCalendar.FindToday(api.Clock.GetUtcNow()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(DataExportController.FileNamePrefix + day + ".json", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var file = await ReadJsonAsync(response);
        Assert.Equal(
            new List<string>
            {
                "exportedAt", "profile", "bankConnections", "bankAccounts", "transactions", "categories", "categorizationRules", "bags",
                "bagMovements", "suggestions", "alertSettings", "alerts", "supportRequests"
            },
            file.EnumerateObject().Select(property => property.Name).ToList());
        Assert.Equal(api.Clock.GetUtcNow().UtcDateTime, file.GetProperty("exportedAt").GetDateTime());
        Assert.Equal(
            (Email, "Durand", "emailAndPhone"),
            (file.GetProperty("profile").GetProperty("email").GetString(),
                file.GetProperty("profile").GetProperty("lastName").GetString(),
                file.GetProperty("profile").GetProperty("notificationChannel").GetString()));
        Assert.Equal("expired", file.GetProperty("bankConnections")[0].GetProperty("status").GetString());
        Assert.Equal(
            ("2026-11-03", -1250),
            (file.GetProperty("transactions")[0].GetProperty("date").GetString(), file.GetProperty("transactions")[0].GetProperty("amountInCents").GetInt32()));
        Assert.Equal(
            ("categoryOverrun", "ignored"),
            (file.GetProperty("suggestions")[0].GetProperty("rule").GetString(), file.GetProperty("suggestions")[0].GetProperty("status").GetString()));
        Assert.Equal(
            ("budgetReached", false),
            (file.GetProperty("alertSettings")[0].GetProperty("type").GetString(), file.GetProperty("alertSettings")[0].GetProperty("isEnabled").GetBoolean()));
        Assert.Equal("bagGoalReached", file.GetProperty("alerts")[0].GetProperty("type").GetString());
        var request = file.GetProperty("supportRequests")[0];
        Assert.Equal(("inProgress", "Réponse"), (request.GetProperty("status").GetString(), request.GetProperty("responses")[0].GetProperty("content").GetString()));
        Assert.Single(api.SecurityEventRepository.SavedSecurityEvents, securityEvent =>
            securityEvent.Type == SecurityEventType.DataExport
            && securityEvent.Result == SecurityEventResult.Success
            && securityEvent.UserId == account.Id);
    }

    // Un mauvais mot de passe est refusé sur son champ, et rien n'est lu.
    [Fact]
    public async Task WrongPasswordIsRefusedOnItsField()
    {
        using var api = new ApiFactoryWithFakes();
        var (client, csrfToken, _) = await LogInWithDataAsync(api);

        var response = await SendSettingsRequestAsync(client, ExportEndpoint, new { password = WrongPassword }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorMessages.WrongCurrentPassword,
            (await ReadJsonAsync(response)).GetProperty("errors").GetProperty(nameof(DataExportRequest.Password))[0].GetString());
        Assert.Empty(api.ExportRepository.ReadUserIds);
    }

    // Avec la double authentification, sans le code, le refus ne dit pas lequel des deux est faux.
    [Fact]
    public async Task TwoFactorAccountWithoutItsCodeGetsTheSharedRefusal()
    {
        using var api = new ApiFactoryWithFakes();
        var activated = await AddActivatedAccountAsync(api);
        api.ExportRepository.SavedData[activated.UserAccount.Id] = CreateData();
        var client = CreateBrowserClient(api);
        var csrfToken = await LogInWithCodeThenGetCsrfTokenAsync(api, client, activated.Secret);

        var response = await SendSettingsRequestAsync(client, ExportEndpoint, new { password = ValidPassword }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorMessages.WrongPasswordOrCode,
            (await ReadJsonAsync(response)).GetProperty("errors").GetProperty(ApiErrorConfiguration.GeneralErrorsKey)[0].GetString());
        Assert.Empty(api.ExportRepository.ReadUserIds);
    }

    // SS §6 : 3 essais par heure et par utilisateur, bons ou mauvais ; le 4e reçoit « trop de requêtes », même avec le bon mot de
    // passe. Une heure plus tard, le fichier se télécharge.
    [Fact]
    public async Task FourthTryInTheHourIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var (client, csrfToken, _) = await LogInWithDataAsync(api);

        for (var tryNumber = 1; tryNumber <= 3; tryNumber++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SendSettingsRequestAsync(client, ExportEndpoint, new { password = WrongPassword }, csrfToken)).StatusCode);
        }

        var fourth = await SendSettingsRequestAsync(client, ExportEndpoint, new { password = ValidPassword }, csrfToken);
        api.Clock.Advance(TimeSpan.FromHours(1));
        var csrfTokenAnHourLater = await LogInThenGetCsrfTokenAsync(client);
        var anHourLater = await SendSettingsRequestAsync(client, ExportEndpoint, new { password = ValidPassword }, csrfTokenAnHourLater);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, anHourLater.StatusCode);
    }

    // SS §6 : sans le jeton anti-CSRF, rien n'est lu ni noté au journal.
    [Fact]
    public async Task ExportNeedsTheCsrfToken()
    {
        using var api = new ApiFactoryWithFakes();
        var (client, _, _) = await LogInWithDataAsync(api);

        var response = await SendSettingsRequestAsync(client, ExportEndpoint, new { password = ValidPassword }, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.ExportRepository.ReadUserIds);
        Assert.DoesNotContain(api.SecurityEventRepository.SavedSecurityEvents, securityEvent => securityEvent.Type == SecurityEventType.DataExport);
    }

    // Sans session, la route répond 401.
    [Fact]
    public async Task ExportNeedsASession()
    {
        using var api = new ApiFactoryWithFakes();

        var response = await api.CreateClient().PostAsync(ExportEndpoint, new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Un mot de passe absent est refusé sur son champ, avant toute vérification.
    [Fact]
    public async Task MissingPasswordIsRefusedOnItsField()
    {
        using var api = new ApiFactoryWithFakes();
        var (client, csrfToken, _) = await LogInWithDataAsync(api);

        var response = await SendSettingsRequestAsync(client, ExportEndpoint, new { }, csrfToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty(nameof(DataExportRequest.Password), out _));
        Assert.Empty(api.ExportRepository.ReadUserIds);
    }
}
