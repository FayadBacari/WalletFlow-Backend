using System.Globalization;

namespace WalletFlow.Infrastructure.Emails;

// Écrit les dates des e-mails en français, en toutes lettres. Les dates reçues sont déjà à l'heure de Paris.
public static class FrenchDates
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    // Écrit le jour : « 1er novembre 2026 », « 22 novembre 2026 ».
    public static string WriteDay(DateOnly day)
    {
        var dayNumber = day.Day == 1 ? "1er" : day.Day.ToString(French);

        return $"{dayNumber} {French.DateTimeFormat.GetMonthName(day.Month)} {day.Year}";
    }

    // Écrit le moment : « le 1er novembre 2026 à 9 h 05 », « le 15 novembre 2026 à 14 h 02 ».
    public static string WriteMoment(DateTime timeInParis)
    {
        return $"le {WriteDay(DateOnly.FromDateTime(timeInParis))} à {timeInParis.Hour} h {timeInParis.Minute:00}";
    }
}
