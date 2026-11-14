namespace WalletFlow.Domain.AccountsAndCategories;

// Le calendrier des utilisateurs, en France : un jour commence à minuit, heure de Paris (GR Dépenses). C'est ce jour-là
// qui compte partout : le mois en cours des graphiques, la date d'un objectif de sac, le jour d'où lire les opérations.
public static class UsersCalendar
{
    public const string TimeZoneId = "Europe/Paris";

    // Donne le jour d'aujourd'hui à Paris.
    public static DateOnly FindToday(DateTimeOffset now)
    {
        var usersTimeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, usersTimeZone).DateTime);
    }
}
