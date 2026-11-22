using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using WalletFlow.Api.Contracts;

namespace WalletFlow.Api.Configuration;

// Un seul format pour toutes les erreurs de l'API (SS §10) : un titre en français et, pour une requête
// refusée, des clés stables, écrites comme les champs du JSON. Aucune réponse ne recopie un nom interne du code
// ni un texte du visiteur.
public static class ApiErrorConfiguration
{
    public const string RefusedRequestTitle = "La requête n'est pas valide.";
    // La clé d'une erreur qui ne vise aucun champ. Elle ne doit pas s'écrire comme un nom de paramètre du code, « request » par exemple.
    public const string GeneralErrorsKey = "general";
    public const string UnreadableRequestMessage = "La requête n'est pas lisible.";
    public const string InvalidValueMessage = "Cette valeur n'est pas valide.";
    public const string CsrfRefusalMessage = "Le jeton anti-CSRF manque ou n'est pas valide. Demande un nouveau jeton, puis réessaie.";
    public const string HttpsRequiredMessage = "L'API ne répond qu'en HTTPS. Envoie la requête à son adresse en https://.";

    // Le nom de l'extension qu'ASP.NET Core ajoute à toute erreur.
    private const string TraceIdKey = "traceId";

    // Remplace les messages d'ASP.NET Core pour une valeur illisible, comme un nombre écrit en lettres dans l'adresse.
    // Ceux d'origine sont en anglais, et recopient la valeur envoyée par le visiteur : chacun devient le même message en français.
    public static void TranslateBindingMessages(DefaultModelBindingMessageProvider messages)
    {
        messages.SetAttemptedValueIsInvalidAccessor((value, field) => InvalidValueMessage);
        messages.SetMissingBindRequiredValueAccessor(field => InvalidValueMessage);
        messages.SetMissingKeyOrValueAccessor(() => InvalidValueMessage);
        messages.SetMissingRequestBodyRequiredValueAccessor(() => InvalidValueMessage);
        messages.SetValueMustNotBeNullAccessor(value => InvalidValueMessage);
        messages.SetUnknownValueIsInvalidAccessor(field => InvalidValueMessage);
        messages.SetValueIsInvalidAccessor(value => InvalidValueMessage);
        messages.SetValueMustBeANumberAccessor(field => InvalidValueMessage);
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => InvalidValueMessage);
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => InvalidValueMessage);
        messages.SetNonPropertyValueMustBeANumberAccessor(() => InvalidValueMessage);
    }

    // Prépare toute erreur avant son envoi : un titre en français, un lien qui décrit son code, et des clés de champ écrites
    // comme dans le JSON. Les erreurs des règles de validation et celles des contrôleurs passent toutes les deux par ici.
    // Le traceId qu'ASP.NET Core ajoute est retiré : ce code tiré au hasard n'est écrit dans aucun journal, il ne sert donc à
    // retrouver aucune panne, et OWASP ZAP y prenait parfois une suite de chiffres pour un numéro de carte bancaire.
    public static void PrepareProblem(ProblemDetailsContext context)
    {
        context.ProblemDetails.Extensions.Remove(TraceIdKey);
        TranslateTitle(context.ProblemDetails);
        context.ProblemDetails.Type ??= FindMissingType(context.ProblemDetails.Status);

        if (context.ProblemDetails is HttpValidationProblemDetails validationProblem)
        {
            WriteKeysLikeJson(validationProblem.Errors);
        }
    }

    // Met en français le titre des erreurs produites par ASP.NET Core.
    private static void TranslateTitle(ProblemDetails problemDetails)
    {
        var frenchTitle = FindFrenchTitle(problemDetails.Status);

        if (frenchTitle != null)
        {
            problemDetails.Title = frenchTitle;
        }
    }

    // Écrit chaque clé comme le champ du JSON que le site envoie : « newEmail », et non « NewEmail ».
    // Deux clés qui s'écrivent alors de la même façon gardent tous leurs messages.
    private static void WriteKeysLikeJson(IDictionary<string, string[]> errors)
    {
        var entries = errors.ToList();
        errors.Clear();

        foreach (var (key, messages) in entries)
        {
            var jsonKey = JsonNamingPolicy.CamelCase.ConvertName(key);
            errors[jsonKey] = errors.TryGetValue(jsonKey, out var earlierMessages) ? [.. earlierMessages, .. messages] : messages;
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

    // Donne le lien qui décrit un code d'erreur, pour le seul code de l'API qu'ASP.NET Core laisse sans lien : 429.
    private static string? FindMissingType(int? statusCode)
    {
        switch (statusCode)
        {
            case StatusCodes.Status429TooManyRequests:
                return "https://tools.ietf.org/html/rfc6585#section-4";
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

        return CreateBadRequest(context.HttpContext, errors);
    }

    // Construit le refus d'une écriture sans jeton anti-CSRF valable (SS §6). Son unique erreur a pour clé le champ qui porte
    // le jeton dans la réponse de GET api/account/csrf-token : le site sait quoi redemander. Le message est le même pour un jeton
    // absent, inventé ou d'un autre compte : il ne dit rien de plus.
    public static IActionResult CreateCsrfRefusalAnswer(HttpContext httpContext)
    {
        var errors = new ModelStateDictionary();
        errors.AddModelError(nameof(CsrfTokenResponse.CsrfToken), CsrfRefusalMessage);

        return CreateBadRequest(httpContext, errors);
    }

    // Construit le refus d'une requête arrivée en HTTP simple (voir HttpsOnlyMiddleware). Son unique erreur ne vise aucun champ :
    // c'est l'adresse de la requête qui est refusée.
    public static IActionResult CreateHttpsRefusalAnswer(HttpContext httpContext)
    {
        var errors = new ModelStateDictionary();
        errors.AddModelError(GeneralErrorsKey, HttpsRequiredMessage);

        return CreateBadRequest(httpContext, errors);
    }

    // Construit une réponse 400 au format des requêtes refusées, avec ces erreurs.
    private static BadRequestObjectResult CreateBadRequest(HttpContext httpContext, ModelStateDictionary errors)
    {
        var problemDetailsFactory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = problemDetailsFactory.CreateValidationProblemDetails(
            httpContext,
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
