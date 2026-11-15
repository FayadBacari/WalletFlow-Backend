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

    // L'heure d'un e-mail est celle de Paris : une heure de plus en hiver, deux en été, et le changement d'heure compte
    // dès 1 h en temps universel (le 29 mars 2026).
    [Theory]
    [InlineData(2026, 11, 4, 12, 0, "2026-11-04T13:00:00")]
    [InlineData(2026, 7, 1, 7, 5, "2026-07-01T09:05:00")]
    [InlineData(2026, 3, 29, 0, 59, "2026-03-29T01:59:00")]
    [InlineData(2026, 3, 29, 1, 0, "2026-03-29T03:00:00")]
    [InlineData(2026, 12, 31, 23, 30, "2027-01-01T00:30:00")]
    public void UsersTimeIsTheTimeInParis(int year, int month, int day, int hour, int minute, string expectedParisTime)
    {
        var utcTime = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

        Assert.Equal(DateTime.Parse(expectedParisTime), UsersCalendar.ConvertToUsersTime(utcTime));
    }
}
