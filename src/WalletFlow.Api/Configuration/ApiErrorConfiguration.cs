using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WalletFlow.Api.Configuration;

// Un seul format pour toutes les erreurs de l'API (SS §10) : un titre en français et, pour une requête
// refusée, des clés stables. Aucune réponse ne recopie un nom interne du code ni un texte du visiteur.
public static class ApiErrorConfiguration
{
    public const string TitleOfEveryRefusedRequestAnswer = "La requête n'est pas valide.";
    public const string KeyOfTheErrorsOutsideTheRequestFields = "Request";
    public const string MessageForRequestThatCannotBeRead = "La requête n'est pas lisible.";

    // Met en français le titre des erreurs produites par ASP.NET Core.
    public static void TranslateTheTitleOfTheProblemIntoFrench(ProblemDetailsContext contextOfTheProblemToTranslate)
    {
        var frenchTitleOfTheProblemToTranslate = FindFrenchTitleForThisStatusCode(contextOfTheProblemToTranslate.ProblemDetails.Status);

        if (frenchTitleOfTheProblemToTranslate != null)
        {
            contextOfTheProblemToTranslate.ProblemDetails.Title = frenchTitleOfTheProblemToTranslate;
        }
    }

    // Donne le titre en français d'un code d'erreur. Un code absent de cette liste garde le titre choisi par sa route.
    private static string? FindFrenchTitleForThisStatusCode(int? statusCodeOfTheProblemToTranslate)
    {
        switch (statusCodeOfTheProblemToTranslate)
        {
            case StatusCodes.Status400BadRequest:
                return TitleOfEveryRefusedRequestAnswer;
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
    public static IActionResult CreateAnswerForTheRefusedRequest(ActionContext contextOfTheRequestBeingRefused)
    {
        var errorsKeptForTheAnswerBeingBuilt = new ModelStateDictionary();

        foreach (var entryOfTheRequestBeingRefused in contextOfTheRequestBeingRefused.ModelState)
        {
            foreach (var errorOfTheEntryBeingRead in entryOfTheRequestBeingRefused.Value.Errors)
            {
                AddOneErrorToTheAnswerBeingBuilt(errorsKeptForTheAnswerBeingBuilt, entryOfTheRequestBeingRefused.Key, errorOfTheEntryBeingRead.ErrorMessage);
            }
        }

        var factoryThatBuildsTheErrorAnswers = contextOfTheRequestBeingRefused.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var detailsOfTheRequestBeingRefused = factoryThatBuildsTheErrorAnswers.CreateValidationProblemDetails(
            contextOfTheRequestBeingRefused.HttpContext,
            errorsKeptForTheAnswerBeingBuilt,
            StatusCodes.Status400BadRequest,
            TitleOfEveryRefusedRequestAnswer);

        return new BadRequestObjectResult(detailsOfTheRequestBeingRefused);
    }

    // Garde le message d'un champ de la requête. Toute autre erreur devient un seul message générique :
    // sa clé pourrait citer un nom interne du code, ou recopier un texte envoyé par le visiteur.
    private static void AddOneErrorToTheAnswerBeingBuilt(ModelStateDictionary errorsKeptForTheAnswerBeingBuilt, string keyOfTheErrorBeingRead, string messageOfTheErrorBeingRead)
    {
        if (CheckThatKeyIsTheNameOfOneRequestField(keyOfTheErrorBeingRead))
        {
            errorsKeptForTheAnswerBeingBuilt.AddModelError(keyOfTheErrorBeingRead, messageOfTheErrorBeingRead);
            return;
        }

        if (!errorsKeptForTheAnswerBeingBuilt.ContainsKey(KeyOfTheErrorsOutsideTheRequestFields))
        {
            errorsKeptForTheAnswerBeingBuilt.AddModelError(KeyOfTheErrorsOutsideTheRequestFields, MessageForRequestThatCannotBeRead);
        }
    }

    // Les champs des requêtes s'écrivent comme Email ou Password : une majuscule, puis des lettres et des chiffres.
    // Le nom d'un paramètre commence par une minuscule, et un chemin JSON par le signe $.
    private static bool CheckThatKeyIsTheNameOfOneRequestField(string keyOfTheErrorBeingRead)
    {
        if (keyOfTheErrorBeingRead.Length == 0 || !char.IsAsciiLetterUpper(keyOfTheErrorBeingRead[0]))
        {
            return false;
        }

        foreach (var characterOfTheKeyBeingRead in keyOfTheErrorBeingRead)
        {
            if (!char.IsAsciiLetterOrDigit(characterOfTheKeyBeingRead))
            {
                return false;
            }
        }

        return true;
    }
}
