using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Api.Configuration;

// Le délai après lequel réessayer, que donne l'en-tête Retry-After d'un refus « trop de requêtes » (429) ou « serveur occupé »
// (503). Pour une limite propre à une action, c'est la durée de sa fenêtre : passé ce délai, la limite est sûrement levée,
// même si elle peut l'être plus tôt (SS §6). Un test vérifie que ces durées restent celles des limites.
public static class RetryDelays
{
    // Inscription, mot de passe oublié et changement d'adresse : 3 demandes par heure et par adresse e-mail.
    public static readonly TimeSpan PerEmailAddress = TimeSpan.FromHours(1);

    // Téléchargement des données : 3 essais par heure et par utilisateur.
    public static readonly TimeSpan DataExport = TimeSpan.FromHours(1);

    // Mise à jour des comptes : 1 par minute et par utilisateur.
    public static readonly TimeSpan BankUpdate = TimeSpan.FromMinutes(1);

    // Demandes de support d'un utilisateur : 10 sur 24 heures glissantes.
    public static readonly TimeSpan SupportRequests = SupportRules.RequestDay;

    // Déplacements d'argent, ceux des suggestions acceptées compris : 1 000 sur 24 heures glissantes.
    public static readonly TimeSpan BagMovements = BagRules.MovementDay;

    // Écrit un délai en secondes entières, arrondi au-dessus : réessayer au moment annoncé n'est jamais trop tôt.
    public static string InSeconds(TimeSpan delay)
    {
        var seconds = (long)Math.Ceiling(delay.TotalSeconds);

        return Math.Max(seconds, 1).ToString(CultureInfo.InvariantCulture);
    }

    // Répond « trop de requêtes » (429) à une limite propre à une action, avec son délai, au même format que les autres refus.
    public static ObjectResult AnswerTooManyRequests(this ControllerBase controller, TimeSpan delay)
    {
        controller.Response.Headers.RetryAfter = InSeconds(delay);

        return controller.Problem(statusCode: StatusCodes.Status429TooManyRequests);
    }
}
