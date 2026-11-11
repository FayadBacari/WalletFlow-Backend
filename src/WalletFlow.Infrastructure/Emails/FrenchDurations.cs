namespace WalletFlow.Infrastructure.Emails;

// Écrit une durée en français dans le texte des e-mails : « 15 minutes », « 1 heure », « 24 heures ».
internal static class FrenchDurations
{
    // Écrit la durée en heures quand elle en compte un nombre entier, sinon en minutes.
    public static string Write(TimeSpan duration)
    {
        var minutes = (int)duration.TotalMinutes;
        if (minutes >= 60 && minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return hours == 1 ? "1 heure" : hours + " heures";
        }

        return minutes == 1 ? "1 minute" : minutes + " minutes";
    }
}
