using System.Globalization;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;

namespace WalletFlow.Infrastructure.Emails;

// Texte en français des alertes de sécurité (GR Notif). Le moment est écrit à l'heure de Paris, en toutes lettres.
public class FrenchSecurityAlertEmails : ISecurityAlertEmails
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    // Crée l'alerte d'une connexion depuis un appareil que le compte ne connaissait pas.
    public EmailMessage CreateNewDeviceEmail(string recipient, DateTime loginTimeInParis)
    {
        var body = $"""
            Bonjour,

            Quelqu'un vient de se connecter à ton compte WalletFlow depuis un appareil que ton compte ne connaissait pas, {WriteMoment(loginTimeInParis)} (heure de Paris).

            Si c'est toi, tu n'as rien à faire. Sinon, change tout de suite ton mot de passe : toutes les autres sessions seront fermées. Active aussi la double authentification, puis écris au support de WalletFlow.
            """;

        return new EmailMessage(recipient, "Nouvelle connexion à ton compte WalletFlow", body);
    }

    // Crée l'alerte d'un changement de mot de passe.
    public EmailMessage CreatePasswordChangedEmail(string recipient, DateTime changeTimeInParis)
    {
        var body = $"""
            Bonjour,

            Le mot de passe de ton compte WalletFlow vient d'être changé, {WriteMoment(changeTimeInParis)} (heure de Paris).

            Si c'est toi, tu n'as rien à faire. Sinon, choisis tout de suite un nouveau mot de passe avec « Mot de passe oublié », puis écris au support de WalletFlow.
            """;

        return new EmailMessage(recipient, "Le mot de passe de ton compte WalletFlow a changé", body);
    }

    // Écrit le moment en français : « le 1er novembre 2026 à 9 h 05 », « le 15 novembre 2026 à 14 h 02 ».
    private static string WriteMoment(DateTime timeInParis)
    {
        var day = timeInParis.Day == 1 ? "1er" : timeInParis.Day.ToString(French);
        var month = French.DateTimeFormat.GetMonthName(timeInParis.Month);

        return $"le {day} {month} {timeInParis.Year} à {timeInParis.Hour} h {timeInParis.Minute:00}";
    }
}
