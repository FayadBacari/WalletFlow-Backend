using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie le calendrier des utilisateurs : un jour commence à minuit, heure de Paris (GR Dépenses).
public class UsersCalendarTests
{
    [Fact]
    public void TimeZoneIsParis()
    {
        Assert.Equal("Europe/Paris", UsersCalendar.TimeZoneId);
    }

    // Un jour commence à minuit, heure de Paris : une heure ou deux avant minuit en temps universel, selon la saison.
    [Theory]
    [InlineData("2026-11-30T22:59:00Z", "2026-11-30")]
    [InlineData("2026-11-30T23:00:00Z", "2026-12-01")]
    [InlineData("2026-06-30T21:59:00Z", "2026-06-30")]
    [InlineData("2026-06-30T22:00:00Z", "2026-07-01")]
    public void TodayIsTheDayInParis(string utcNow, string expectedDay)
    {
        Assert.Equal(DateOnly.Parse(expectedDay), UsersCalendar.FindToday(DateTimeOffset.Parse(utcNow)));
    }
}
