using System.Globalization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WalletFlow.Api.Filters;

// Donne la même durée minimale à chaque réponse de la connexion et de l'inscription (SS §2). Une adresse inconnue, un compte
// non confirmé et un compte confirmé ne demandent pas le même travail en base : sans ce délai, le temps de réponse dirait si un compte
// utilise l'adresse. Le hachage Argon2id, lui, est déjà fait dans tous les cas.
public class MinimumAnswerDurationFilter : IAsyncActionFilter
{
    // Le réglage qui donne la durée minimale, en millisecondes.
    public const string SettingName = "MinimumAnswerDurationInMilliseconds";

    // Sans réglage : assez long pour couvrir un hachage Argon2id et quelques lectures en base sur un petit serveur.
    public static readonly TimeSpan DefaultMinimumDuration = TimeSpan.FromMilliseconds(500);

    private readonly TimeSpan minimumDuration;
    private readonly TimeProvider clock;

    // Reçoit la durée minimale et l'horloge.
    public MinimumAnswerDurationFilter(TimeSpan minimumDuration, TimeProvider clock)
    {
        this.minimumDuration = minimumDuration;
        this.clock = clock;
    }

    // Lit la durée minimale dans la configuration. Sans réglage, la durée par défaut s'applique ;
    // un réglage illisible ou négatif empêche l'API de démarrer.
    public static TimeSpan ReadMinimumDuration(IConfiguration configuration)
    {
        var settingText = configuration[SettingName];

        if (string.IsNullOrWhiteSpace(settingText))
        {
            return DefaultMinimumDuration;
        }

        if (!int.TryParse(settingText, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds))
        {
            throw new InvalidOperationException(
                "Le réglage « " + SettingName + " » doit être un nombre entier de millisecondes, positif ou nul.");
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    // Laisse la connexion se faire, puis attend ce qui manque pour atteindre la durée minimale, que la connexion réussisse ou non.
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var startTimestamp = clock.GetTimestamp();

        await next();

        var elapsed = clock.GetElapsedTime(startTimestamp);

        if (elapsed < minimumDuration)
        {
            await Task.Delay(minimumDuration - elapsed, clock);
        }
    }
}
