using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie les réglages d'alerte (GR Notif, DD §9.2) : le canal, et chaque type activé ou non. Une alerte de sécurité
// ne se désactive jamais, et un type sans choix enregistré est activé.
public class AlertSettingsServiceTests
{
    private readonly FakeUserAccountRepository accounts = new FakeUserAccountRepository();
    private readonly FakeAlertRepository alerts = new FakeAlertRepository();
    private readonly FakeDatabaseUnitOfWork unitOfWork;
    private readonly User user = new User { Email = "reglages@exemple.test", PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };

    // Garde le compte des tests, et relie les fausses bases à la fausse unité de travail.
    public AlertSettingsServiceTests()
    {
        accounts.SavedUsers.Add(user);
        unitOfWork = new FakeDatabaseUnitOfWork(accounts, alerts);
    }

    private AlertSettingsService CreateService()
    {
        return new AlertSettingsService(alerts, accounts, new DatabaseChangesSavingService(unitOfWork));
    }

    private async Task<AlertSettingsResult> SwitchAsync(AlertKind kind, bool enabled)
    {
        return await CreateService().SwitchAsync(user.Id, kind, enabled);
    }

    // Le choix enregistré de ce type, d'après les réglages relus.
    private static bool IsEnabledIn(AlertSettings settings, AlertKind kind)
    {
        return settings.Alerts.Single(setting => setting.Kind == kind).IsEnabled;
    }

    [Fact]
    public async Task SettingsGiveTheTenTypesInListOrderAllEnabledByDefault()
    {
        var settings = await CreateService().ReadAsync(user.Id);

        Assert.NotNull(settings);
        Assert.Equal(NotificationChannel.Email, settings.Channel);
        Assert.Equal(Enum.GetValues<AlertKind>().ToList(), settings.Alerts.Select(setting => setting.Kind).ToList());
        Assert.All(settings.Alerts, setting => Assert.True(setting.IsEnabled));
        Assert.Equal(
            new List<bool> { true, true, true, true, false, false, false, false, false, false },
            settings.Alerts.Select(setting => setting.CanBeDisabled).ToList());
        Assert.Equal("Budget d'une catégorie atteint", settings.Alerts[0].Label);
    }

    // Couper puis remettre une alerte garde un seul choix, qui change.
    [Fact]
    public async Task AlertIsSwitchedOffThenOnWithASingleChoice()
    {
        var switchedOff = await SwitchAsync(AlertKind.BudgetReached, false);
        Assert.False(IsEnabledIn(Assert.IsType<AlertSettingsResult.Saved>(switchedOff).Settings, AlertKind.BudgetReached));

        var switchedOn = await SwitchAsync(AlertKind.BudgetReached, true);

        Assert.True(IsEnabledIn(Assert.IsType<AlertSettingsResult.Saved>(switchedOn).Settings, AlertKind.BudgetReached));
        var preference = Assert.Single(alerts.SavedPreferences);
        Assert.Equal(user.Id, preference.UserId);
        Assert.True(preference.IsEnabled);
    }

    // Couper un type ne touche que lui.
    [Fact]
    public async Task SwitchingOneTypeLeavesTheOthersEnabled()
    {
        var result = await SwitchAsync(AlertKind.NewSuggestion, false);

        var settings = Assert.IsType<AlertSettingsResult.Saved>(result).Settings;
        Assert.Equal(
            new List<AlertKind> { AlertKind.NewSuggestion },
            settings.Alerts.Where(setting => !setting.IsEnabled).Select(setting => setting.Kind).ToList());
    }

    // GR Notif : les alertes de sécurité ne peuvent pas être désactivées. Rien n'est enregistré.
    [Theory]
    [InlineData(AlertKind.NewDevice)]
    [InlineData(AlertKind.AccountLocked)]
    [InlineData(AlertKind.PasswordChange)]
    [InlineData(AlertKind.EmailChange)]
    [InlineData(AlertKind.TwoFactorDeactivation)]
    [InlineData(AlertKind.BackupCodeUse)]
    public async Task SecurityAlertCannotBeDisabled(AlertKind kind)
    {
        var result = await SwitchAsync(kind, false);

        Assert.IsType<AlertSettingsResult.CannotBeDisabled>(result);
        Assert.Empty(alerts.SavedPreferences);
        Assert.Equal(0, unitOfWork.CommittedTransactionCount);
    }

    // Demander qu'une alerte de sécurité soit activée ne change rien : elle l'est toujours.
    [Fact]
    public async Task EnablingASecurityAlertKeepsEverythingEnabled()
    {
        var result = await SwitchAsync(AlertKind.NewDevice, true);

        Assert.All(Assert.IsType<AlertSettingsResult.Saved>(result).Settings.Alerts, setting => Assert.True(setting.IsEnabled));
    }

    // GR Notif : le choix du téléphone est gardé pour plus tard.
    [Fact]
    public async Task ChosenChannelIsKept()
    {
        var result = await CreateService().ChangeChannelAsync(user.Id, NotificationChannel.EmailAndPhone);

        Assert.Equal(NotificationChannel.EmailAndPhone, Assert.IsType<AlertSettingsResult.Saved>(result).Settings.Channel);
        Assert.Equal(NotificationChannel.EmailAndPhone, user.NotificationChannel);
        Assert.Equal(1, unitOfWork.CommittedTransactionCount);
    }

    // Le choix d'un autre utilisateur ne change pas les réglages de celui-ci.
    [Fact]
    public async Task ChoiceOfAnotherUserDoesNotCount()
    {
        var budgetType = alerts.SavedTypes.Single(type => type.Kind == AlertKind.BudgetReached);
        alerts.SavedPreferences.Add(AlertPreference.Create(Guid.NewGuid(), budgetType, false));

        var settings = await CreateService().ReadAsync(user.Id);

        Assert.NotNull(settings);
        Assert.True(IsEnabledIn(settings, AlertKind.BudgetReached));
    }

    [Fact]
    public async Task AccountThatNoLongerExistsHasNoSettings()
    {
        accounts.SavedUsers.Clear();

        Assert.Null(await CreateService().ReadAsync(user.Id));
        Assert.IsType<AlertSettingsResult.AccountGone>(await SwitchAsync(AlertKind.BudgetReached, false));
        Assert.IsType<AlertSettingsResult.AccountGone>(await CreateService().ChangeChannelAsync(user.Id, NotificationChannel.Phone));
        Assert.Empty(alerts.SavedPreferences);
    }

    // Une autre requête crée le même premier choix au même instant : la clé de la table fait échouer celui-ci,
    // qui relit et change le choix de l'autre. Il reste un seul choix, le dernier demandé.
    [Fact]
    public async Task SimultaneousFirstChoiceChangesTheOtherOne()
    {
        var budgetType = alerts.SavedTypes.Single(type => type.Kind == AlertKind.BudgetReached);
        alerts.PreferenceSavedBeforeSave = AlertPreference.Create(user.Id, budgetType, true);

        var result = await SwitchAsync(AlertKind.BudgetReached, false);

        Assert.IsType<AlertSettingsResult.Saved>(result);
        Assert.False(Assert.Single(alerts.SavedPreferences).IsEnabled);
    }

    // Après 10 conflits, rien n'est enregistré.
    [Fact]
    public async Task EndlessConflictsSaveNothing()
    {
        alerts.Conflicts = DatabaseChangesSavingService.MaximumTries;

        var result = await SwitchAsync(AlertKind.BudgetReached, false);

        Assert.IsType<AlertSettingsResult.NotSaved>(result);
        Assert.Empty(alerts.SavedPreferences);
    }
}
