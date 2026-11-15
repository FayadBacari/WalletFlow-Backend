using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Tests.Domain;

// Vérifie qu'une alerte et son e-mail ne portent que ce que leur type demande (DD §9.3 et §9.4, ck_alerte_contenu).
public class AlertTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly November = new DateOnly(2026, 11, 1);

    [Fact]
    public void BudgetAlertNamesItsCategoryMonthAndThresholdOnly()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var alert = Alert.ForBudget(userId, categoryId, November, 80, Now);

        Assert.Equal(AlertKind.BudgetReached, alert.Kind);
        Assert.Equal(userId, alert.UserId);
        Assert.Equal(categoryId, alert.CategoryId);
        Assert.Equal(November, alert.Month);
        Assert.Equal((short)80, alert.Threshold);
        Assert.Equal(Now, alert.CreatedAt);
        Assert.Null(alert.BagId);
        Assert.Null(alert.ConnectionId);
    }

    // Seuls 80 et 100 sont des seuils (DD §9.3).
    [Theory]
    [InlineData((short)0)]
    [InlineData((short)79)]
    [InlineData((short)90)]
    [InlineData((short)101)]
    public void BudgetAlertRefusesAnyOtherThreshold(short threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Alert.ForBudget(Guid.NewGuid(), Guid.NewGuid(), November, threshold, Now));
    }

    // Un mois est noté par son premier jour (ck_alerte_mois).
    [Fact]
    public void MonthAlertsRefuseAnyOtherDay()
    {
        var secondDay = new DateOnly(2026, 11, 2);

        Assert.Throws<ArgumentException>(() => Alert.ForBudget(Guid.NewGuid(), Guid.NewGuid(), secondDay, 100, Now));
        Assert.Throws<ArgumentException>(() => Alert.ForSuggestions(Guid.NewGuid(), secondDay, Now));
    }

    [Fact]
    public void SuggestionAlertNamesItsMonthOnly()
    {
        var alert = Alert.ForSuggestions(Guid.NewGuid(), November, Now);

        Assert.Equal(AlertKind.NewSuggestion, alert.Kind);
        Assert.Equal(November, alert.Month);
        Assert.Null(alert.Threshold);
        Assert.Null(alert.CategoryId);
        Assert.Null(alert.BagId);
        Assert.Null(alert.ConnectionId);
    }

    [Fact]
    public void BagAndConnectionAlertsNameTheirTargetOnly()
    {
        var bagId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();

        var bagAlert = Alert.ForBagGoal(Guid.NewGuid(), bagId, Now);
        var connectionAlert = Alert.ForBankAuthorization(Guid.NewGuid(), connectionId, Now);

        Assert.Equal(AlertKind.BagGoalReached, bagAlert.Kind);
        Assert.Equal(bagId, bagAlert.BagId);
        Assert.Null(bagAlert.Month);
        Assert.Null(bagAlert.ConnectionId);
        Assert.Equal(AlertKind.BankAuthorizationToRenew, connectionAlert.Kind);
        Assert.Equal(connectionId, connectionAlert.ConnectionId);
        Assert.Null(connectionAlert.Month);
        Assert.Null(connectionAlert.BagId);
    }

    // L'e-mail d'une alerte vise le compte de l'alerte, sans adresse ni adresse IP, et part tout de suite (ck_demande_email_*).
    [Fact]
    public void AlertEmailIsLinkedToItsAlertAndLeavesAtOnce()
    {
        var alert = Alert.ForBagGoal(Guid.NewGuid(), Guid.NewGuid(), Now);

        var request = EmailRequest.ForAlert(alert);

        Assert.Equal(EmailRequestType.Alert, request.Type);
        Assert.Equal(alert.UserId, request.UserId);
        Assert.Equal(alert.Id, request.AlertId);
        Assert.Null(request.EmailAddress);
        Assert.Null(request.IpAddress);
        Assert.Equal(Now, request.RequestedAt);
        Assert.Equal(Now, request.NextTryAt);
        Assert.Equal(0, request.FailedTryCount);
    }
}
