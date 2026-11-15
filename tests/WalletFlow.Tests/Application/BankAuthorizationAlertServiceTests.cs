using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie l'alerte d'une autorisation bancaire à renouveler (GR Comptes, DD §9.3) : 7 jours avant la fin, une seule fois
// par autorisation, et effacée par le renouvellement. Et l'effacement des vieilles alertes (SS §7).
public class BankAuthorizationAlertServiceTests
{
    private readonly BankServices services = new BankServices();

    // Garde une connexion de l'utilisateur dont l'autorisation finit dans ce délai.
    private BankConnection SaveConnection(TimeSpan timeBeforeEnd)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = "session-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = services.Now + timeBeforeEnd,
            UserId = services.UserId
        };
        services.Repository.SavedConnections.Add(connection);

        return connection;
    }

    // Les e-mails d'alerte mis dans la file.
    private List<EmailRequest> QueuedAlertEmails()
    {
        return services.EmailRequests.SavedRequests.Where(request => request.Type == EmailRequestType.Alert).ToList();
    }

    // Cherche les connexions à prévenir, puis prévient chacune, comme la tâche de fond.
    private async Task<List<Guid>> WarnDueConnectionsAsync()
    {
        var alertService = services.CreateAuthorizationAlerts();
        var warnedIds = new List<Guid>();

        foreach (var connectionId in await alertService.FindConnectionsToWarnAsync())
        {
            if (await alertService.WarnAsync(connectionId))
            {
                warnedIds.Add(connectionId);
            }
        }

        return warnedIds;
    }

    // L'utilisateur est prévenu à partir de 7 jours avant la fin, et une autorisation déjà finie sans alerte l'est aussi.
    [Fact]
    public async Task AuthorizationEndingWithinSevenDaysIsWarnedOnce()
    {
        var tooEarly = SaveConnection(TimeSpan.FromDays(7).Add(TimeSpan.FromSeconds(1)));
        var dueNow = SaveConnection(TimeSpan.FromDays(7));
        var alreadyEnded = SaveConnection(TimeSpan.FromDays(-2));

        var foundIds = await services.CreateAuthorizationAlerts().FindConnectionsToWarnAsync();
        var warnedIds = await WarnDueConnectionsAsync();

        Assert.Equal(new HashSet<Guid> { dueNow.Id, alreadyEnded.Id }, foundIds.ToHashSet());

        Assert.Equal(new HashSet<Guid> { dueNow.Id, alreadyEnded.Id }, warnedIds.ToHashSet());
        Assert.DoesNotContain(tooEarly.Id, warnedIds);
        Assert.Equal(2, QueuedAlertEmails().Count);
        var alert = services.Alerts.SavedAlerts.Single(saved => saved.ConnectionId == dueNow.Id);
        Assert.Equal(AlertKind.BankAuthorizationToRenew, alert.Kind);
        Assert.Equal(services.UserId, alert.UserId);
        Assert.Equal(services.Now, alert.CreatedAt);
    }

    // Une seule alerte par autorisation : le passage suivant ne trouve plus rien, et un essai direct n'envoie rien.
    [Fact]
    public async Task AuthorizationIsNeverWarnedTwice()
    {
        var connection = SaveConnection(TimeSpan.FromDays(3));
        await WarnDueConnectionsAsync();
        services.Clock.Advance(TimeSpan.FromHours(1));

        var warnedAgain = await WarnDueConnectionsAsync();
        var directTry = await services.CreateAuthorizationAlerts().WarnAsync(connection.Id);

        Assert.Empty(warnedAgain);
        Assert.False(directTry);
        Assert.Single(QueuedAlertEmails());
    }

    // L'alerte réécrit la connexion avec sa version : un renouvellement enregistré au même instant la fait recommencer,
    // et la nouvelle date de fin, loin dans 180 jours, n'envoie rien.
    [Fact]
    public async Task RenewalSavedAtTheSameMomentCancelsTheWarning()
    {
        var connection = SaveConnection(TimeSpan.FromDays(3));
        services.Repository.OtherRequestBeforeCheckedSave = checkedConnection =>
            checkedConnection.Renew("session-renouvelee", services.Now.AddDays(180));

        var isWarned = await services.CreateAuthorizationAlerts().WarnAsync(connection.Id);

        Assert.False(isWarned);
        Assert.Empty(services.Alerts.SavedAlerts);
        Assert.Empty(QueuedAlertEmails());
        Assert.Equal("session-renouvelee", connection.ExternalConnectionId);
    }

    // L'alerte enregistrée vérifie la version de sa connexion.
    [Fact]
    public async Task WarningRewritesItsConnectionWithItsVersion()
    {
        var connection = SaveConnection(TimeSpan.FromDays(3));

        await services.CreateAuthorizationAlerts().WarnAsync(connection.Id);

        Assert.Equal(connection, Assert.Single(services.Repository.CommittedChecks));
    }

    // DD §9.3 : l'alerte désactivée n'est pas créée, et la connexion n'est même pas proposée à la tâche de fond.
    [Fact]
    public async Task DisabledAuthorizationAlertIsNotCreated()
    {
        var connection = SaveConnection(TimeSpan.FromDays(3));
        var authorizationType = services.Alerts.SavedTypes.Single(type => type.Kind == AlertKind.BankAuthorizationToRenew);
        services.Alerts.SavedPreferences.Add(AlertPreference.Create(services.UserId, authorizationType, false));

        var warnedIds = await WarnDueConnectionsAsync();
        var directTry = await services.CreateAuthorizationAlerts().WarnAsync(connection.Id);

        Assert.Empty(warnedIds);
        Assert.False(directTry);
        Assert.Empty(services.Alerts.SavedAlerts);
    }

    // Une connexion supprimée entre la liste et l'alerte n'envoie rien.
    [Fact]
    public async Task DeletedConnectionIsNotWarned()
    {
        var connection = SaveConnection(TimeSpan.FromDays(3));
        services.Repository.SavedConnections.Clear();

        var isWarned = await services.CreateAuthorizationAlerts().WarnAsync(connection.Id);

        Assert.False(isWarned);
        Assert.Empty(services.Alerts.SavedAlerts);
    }

    // GR Comptes : renouveler l'autorisation efface son alerte ; la nouvelle préviendra à son tour, 7 jours avant sa fin.
    [Fact]
    public async Task RenewalErasesTheAlertAndTheNewAuthorizationIsWarnedInItsTurn()
    {
        var connection = await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        services.Clock.Advance(TimeSpan.FromDays(175));
        await WarnDueConnectionsAsync();
        Assert.Single(services.Alerts.SavedAlerts);
        services.Provider.SessionValidUntil = services.Now.AddDays(180).AddMinutes(-10);
        await services.CreateStart().StartRenewalAsync(services.UserId, connection.Id);
        services.Provider.AddSession("code-2", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));

        var result = await services.CreateFinish().FinishAsync(
            services.UserId, "code-2", services.Provider.AuthorizationRequests.Last().State, services.Presence);

        Assert.IsType<BankConnectionFinishResult.Renewed>(result);
        Assert.Empty(services.Alerts.SavedAlerts);
        Assert.Empty(await services.CreateAuthorizationAlerts().FindConnectionsToWarnAsync());

        services.Clock.Advance(TimeSpan.FromDays(174));
        Assert.Equal(new List<Guid> { connection.Id }, await WarnDueConnectionsAsync());
        Assert.Equal(2, QueuedAlertEmails().Count);
    }

    // SS §7 : les alertes de budget et de suggestion sont effacées deux mois après leur mois. Le 4 novembre, celles d'août
    // partent, celles de septembre restent, comme celles d'un sac ou d'une connexion, qui ne partent qu'avec eux.
    [Fact]
    public async Task OldBudgetAndSuggestionAlertsAreErasedTwoMonthsAfterTheirMonth()
    {
        var august = new DateOnly(2026, 8, 1);
        var september = new DateOnly(2026, 9, 1);
        var categoryId = services.CategoryNamed("Loisirs").Id;
        services.Alerts.SavedAlerts.AddRange(new[]
        {
            Alert.ForBudget(services.UserId, categoryId, august, 80, services.Now),
            Alert.ForSuggestions(services.UserId, august, services.Now),
            Alert.ForBudget(services.UserId, categoryId, september, 100, services.Now),
            Alert.ForSuggestions(services.UserId, september, services.Now),
            Alert.ForBagGoal(services.UserId, Guid.NewGuid(), services.Now.AddYears(-1)),
            Alert.ForBankAuthorization(services.UserId, Guid.NewGuid(), services.Now.AddYears(-1)),
        });

        var deletedCount = await new AlertCleanupService(services.Alerts, services.Clock).DeleteOldAlertsAsync();

        Assert.Equal(2, deletedCount);
        Assert.Equal(4, services.Alerts.SavedAlerts.Count);
        Assert.DoesNotContain(services.Alerts.SavedAlerts, alert => alert.Month == august);
    }

    // Le mois se lit à Paris : le 31 décembre à 23 h 30 en temps universel, c'est déjà janvier, et octobre part.
    [Fact]
    public async Task CleanupReadsTheMonthInParis()
    {
        var clock = new FakeTimeProvider();
        clock.Advance(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero) - clock.GetUtcNow());
        var october = new DateOnly(2026, 10, 1);
        services.Alerts.SavedAlerts.Add(Alert.ForSuggestions(services.UserId, october, services.Now));

        var deletedCount = await new AlertCleanupService(services.Alerts, clock).DeleteOldAlertsAsync();

        Assert.Equal(1, deletedCount);
    }
}
