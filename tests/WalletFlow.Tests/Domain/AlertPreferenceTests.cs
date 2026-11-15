using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les choix d'alerte d'un utilisateur (GR Notif, DD §9.2) : une alerte de sécurité ne se désactive jamais.
public class AlertPreferenceTests
{
    private static readonly AlertType BudgetType = new AlertType { Kind = AlertKind.BudgetReached, Label = "Budget", CanBeDisabled = true };
    private static readonly AlertType DeviceType = new AlertType { Kind = AlertKind.NewDevice, Label = "Appareil", CanBeDisabled = false };

    [Fact]
    public void AlertThatCanBeDisabledIsSwitchedOffThenOn()
    {
        var preference = AlertPreference.Create(Guid.NewGuid(), BudgetType, false);
        Assert.False(preference.IsEnabled);
        Assert.Equal(AlertKind.BudgetReached, preference.Kind);

        preference.Switch(BudgetType, true);

        Assert.True(preference.IsEnabled);
    }

    // Ni à la création, ni ensuite, et le choix reste activé.
    [Fact]
    public void SecurityAlertIsNeverDisabled()
    {
        Assert.Throws<InvalidOperationException>(() => AlertPreference.Create(Guid.NewGuid(), DeviceType, false));

        var preference = AlertPreference.Create(Guid.NewGuid(), DeviceType, true);

        Assert.Throws<InvalidOperationException>(() => preference.Switch(DeviceType, false));
        Assert.True(preference.IsEnabled);
    }

    // Le type donné doit être celui du choix : sinon, le droit de désactiver serait lu sur un autre type.
    [Fact]
    public void SwitchRefusesAnotherType()
    {
        var preference = AlertPreference.Create(Guid.NewGuid(), DeviceType, true);

        Assert.Throws<ArgumentException>(() => preference.Switch(BudgetType, false));
        Assert.True(preference.IsEnabled);
    }

    // Le canal choisi est gardé, même le téléphone, qui attend l'application mobile (GR Notif).
    [Theory]
    [InlineData(NotificationChannel.Phone)]
    [InlineData(NotificationChannel.EmailAndPhone)]
    [InlineData(NotificationChannel.Email)]
    public void ChosenChannelIsKept(NotificationChannel channel)
    {
        var user = new User { Email = "canal@exemple.test" };

        user.ChangeNotificationChannel(channel);

        Assert.Equal(channel, user.NotificationChannel);
    }

    [Fact]
    public void ChannelOutsideTheListIsRefused()
    {
        var user = new User { Email = "canal@exemple.test" };

        Assert.Throws<ArgumentOutOfRangeException>(() => user.ChangeNotificationChannel((NotificationChannel)3));
        Assert.Equal(NotificationChannel.Email, user.NotificationChannel);
    }
}
