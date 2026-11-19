using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie la date de dernière connexion d'un appareil (SS §3).
public class DeviceTests
{
    private static readonly DateTime LastUse = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Deux connexions peuvent se noter dans le désordre : la date ne recule jamais, sinon l'appareil serait oublié trop tôt.
    [Fact]
    public void LastUseNeverGoesBackInTime()
    {
        var device = new Device { LastUsedAt = LastUse };

        device.RecordUse(LastUse.AddDays(-5));
        Assert.Equal(LastUse, device.LastUsedAt);

        device.RecordUse(LastUse.AddDays(5));
        Assert.Equal(LastUse.AddDays(5), device.LastUsedAt);
    }
}
