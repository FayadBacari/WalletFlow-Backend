using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WalletFlow.Api.Configuration;

// Un seul format pour toutes les erreurs de l'API (SS §10) : un titre en français et, pour une requête
// refusée, des clés stables. Aucune réponse ne recopie un nom interne du code ni un texte du visiteur.
public static class ApiErrorConfiguration
{
    public const string RefusedRequestTitle = "La requête n'est pas valide.";
    public const string GeneralErrorsKey = "Request";
    public const string UnreadableRequestMessage = "La requête n'est pas lisible.";

    // Met en français le titre des erreurs produites par ASP.NET Core.
    public static void TranslateTitle(ProblemDetailsContext context)
    {
        var frenchTitle = FindFrenchTitle(context.ProblemDetails.Status);

        if (frenchTitle != null)
        {
            context.ProblemDetails.Title = frenchTitle;
        }
    }

    // Donne le titre en français d'un code d'erreur. Un code absent de cette liste garde le titre choisi par sa route.
    private static string? FindFrenchTitle(int? statusCode)
    {
        switch (statusCode)
        {
            case StatusCodes.Status400BadRequest:
                return RefusedRequestTitle;
            case StatusCodes.Status404NotFound:
                return "Cette adresse n'existe pas.";
            case StatusCodes.Status405MethodNotAllowed:
                return "Cette action n'est pas possible à cette adresse.";
            case StatusCodes.Status413PayloadTooLarge:
                return "La requête est trop volumineuse.";
            case StatusCodes.Status415UnsupportedMediaType:
                return "Le format de la requête n'est pas accepté.";
            case StatusCodes.Status429TooManyRequests:
                return "Trop de demandes. Réessaie plus tard.";
            case StatusCodes.Status500InternalServerError:
                return "Une erreur est survenue. Réessaie plus tard.";
            default:
                return null;
        }
    }

    // Construit la réponse de toute requête refusée : celle des règles de validation, et celle des contrôleurs.
    public static IActionResult CreateRefusedRequestAnswer(ActionContext context)
    {
        var errors = new ModelStateDictionary();

        foreach (var entry in context.ModelState)
        {
            foreach (var error in entry.Value.Errors)
            {
                AddError(errors, entry.Key, error.ErrorMessage);
            }
        }

        var problemDetailsFactory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = problemDetailsFactory.CreateValidationProblemDetails(
            context.HttpContext,
            errors,
            StatusCodes.Status400BadRequest,
            RefusedRequestTitle);

        return new BadRequestObjectResult(problemDetails);
    }

    // Garde le message d'un champ de la requête. Toute autre erreur devient un seul message générique :
    // sa clé pourrait citer un nom interne du code, ou recopier un texte envoyé par le visiteur.
    private static void AddError(ModelStateDictionary errors, string key, string message)
    {
        if (IsRequestField(key))
        {
            errors.AddModelError(key, message);
            return;
        }

        if (!errors.ContainsKey(GeneralErrorsKey))
        {
            errors.AddModelError(GeneralErrorsKey, UnreadableRequestMessage);
        }
    }

    // Les champs des requêtes s'écrivent comme Email ou Password : une majuscule, puis des lettres et des chiffres.
    // Le nom d'un paramètre commence par une minuscule, et un chemin JSON par le signe $.
    private static bool IsRequestField(string key)
    {
        if (key.Length == 0 || !char.IsAsciiLetterUpper(key[0]))
        {
            return false;
        }

        foreach (var character in key)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
