using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;
using WalletFlow.Api.Filters;

namespace WalletFlow.Api.Configuration;

// Décrit dans OpenAPI chaque réponse possible d'une route, pour que le site sache quoi attendre (SS §10). Une action annonce sa
// réponse réussie et ses refus propres (404, 409, 503…) par ses attributs ProducesResponseType. Ce fichier ajoute les refus que
// la route reçoit de la chaîne commune, lus dans ses propres réglages : la session, le jeton anti-CSRF, le corps, sa taille, le
// rôle et les limites de débit. Ils ne peuvent donc pas s'écarter de ce que la route fait vraiment. Les valeurs passées après
// le « ? » y sont nommées comme les champs du JSON.
public static class OpenApiConfiguration
{
    public const string WrongCredentialsDescription = "L'adresse e-mail ou le mot de passe est faux.";
    public const string EmailNotConfirmedDescription = "L'adresse e-mail n'est pas encore confirmée.";
    public const string LoginMustRestartDescription =
        "La connexion doit recommencer : le 5e code était faux, l'attente a dépassé 5 minutes, ou le compte a changé pendant l'attente.";
    public const string ServerBusyDescription = "Le serveur est très demandé : réessaie après le délai de l'en-tête Retry-After.";
    public const string RegistrationUnavailableDescription =
        "Le serveur est très demandé, ou l'e-mail de confirmation n'a pas pu partir : réessaie plus tard.";
    public const string BankUnavailableDescription = "La banque ou le prestataire bancaire ne répond pas : réessaie plus tard.";

    private const string JsonMediaType = "application/json";
    private const string ProblemMediaType = "application/problem+json";
    private const string ProblemSchemaName = "ProblemDetails";
    private const string ValidationProblemSchemaName = "HttpValidationProblemDetails";

    // Ajoute la description des réponses et le nom des valeurs de l'adresse à la génération du document OpenAPI.
    public static void ApplyOptions(OpenApiOptions options)
    {
        options.AddOperationTransformer(DescribeResponsesAsync);
        options.AddOperationTransformer(NameQueryValuesLikeJsonAsync);
    }

    // Nomme chaque valeur passée après le « ? » comme le champ JSON du même nom : « from », et non « From ». ASP.NET Core les lit
    // sans tenir compte de la casse : le site les écrit comme le reste de l'API (nom de la signature imposé par ASP.NET Core).
    private static Task NameQueryValuesLikeJsonAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        foreach (var parameter in operation.Parameters ?? [])
        {
            if (parameter is OpenApiParameter { In: ParameterLocation.Query, Name: not null } queryValue)
            {
                queryValue.Name = JsonNamingPolicy.CamelCase.ConvertName(queryValue.Name);
            }
        }

        return Task.CompletedTask;
    }

    // Complète les réponses d'une route : les refus de la chaîne commune, le format « problem details » de chaque erreur, et
    // une description en français de chaque réponse (nom de la signature imposé par ASP.NET Core).
    private static async Task DescribeResponsesAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var route = context.Description;
        var responses = operation.Responses ??= new OpenApiResponses();

        foreach (var statusCode in ListCommonCodes(route))
        {
            responses.TryAdd(statusCode.ToString(CultureInfo.InvariantCulture), new OpenApiResponse());
        }

        var problemSchema = await ReferenceSchemaAsync(context, typeof(ProblemDetails), ProblemSchemaName, cancellationToken);
        var validationSchema = await ReferenceSchemaAsync(context, typeof(HttpValidationProblemDetails), ValidationProblemSchemaName, cancellationToken);

        foreach (var (code, answer) in responses.ToList())
        {
            if (answer is not OpenApiResponse response)
            {
                continue;
            }

            var statusCode = int.Parse(code, CultureInfo.InvariantCulture);

            if (statusCode >= StatusCodes.Status400BadRequest)
            {
                var schema = statusCode == StatusCodes.Status400BadRequest ? validationSchema : problemSchema;
                response.Content = new Dictionary<string, OpenApiMediaType> { [ProblemMediaType] = new OpenApiMediaType { Schema = schema } };
                AddRetryDelay(response, statusCode);
            }
            else
            {
                KeepOnlyJson(response);
            }

            if (string.IsNullOrEmpty(response.Description) || response.Description == ReasonPhrases.GetReasonPhrase(statusCode))
            {
                response.Description = Describe(statusCode, route);
            }
        }
    }

    // Donne les refus que la chaîne commune peut opposer à cette route, avant ou autour de son action.
    private static List<int> ListCommonCodes(ApiDescription route)
    {
        var metadata = route.ActionDescriptor.EndpointMetadata;
        var codes = new List<int> { StatusCodes.Status429TooManyRequests, StatusCodes.Status500InternalServerError };

        if (ReadsBody(route) || ReadsQuery(route) || NeedsCsrfToken(route))
        {
            codes.Add(StatusCodes.Status400BadRequest);
        }

        if (!metadata.OfType<IAllowAnonymous>().Any())
        {
            codes.Add(StatusCodes.Status401Unauthorized);
        }

        if (metadata.OfType<ServiceFilterAttribute>().Any(filter => filter.ServiceType == typeof(AdministratorOnlyFilter)))
        {
            codes.Add(StatusCodes.Status403Forbidden);
        }

        if (metadata.OfType<IRequestSizeLimitMetadata>().Any())
        {
            codes.Add(StatusCodes.Status413PayloadTooLarge);
        }

        if (ReadsBody(route))
        {
            codes.Add(StatusCodes.Status415UnsupportedMediaType);
        }

        return codes;
    }

    // Vrai si la route lit un corps JSON : il peut être illisible, refusé par ses règles, ou d'un autre format.
    private static bool ReadsBody(ApiDescription route)
    {
        return route.ParameterDescriptions.Any(parameter => parameter.Source == BindingSource.Body);
    }

    // Vrai si la route lit des valeurs dans son adresse après le « ? » : une valeur illisible y est refusée.
    private static bool ReadsQuery(ApiDescription route)
    {
        return route.ParameterDescriptions.Any(parameter => parameter.Source == BindingSource.Query);
    }

    // Vrai si la route demande le jeton anti-CSRF : toute écriture, sauf celles qui disent l'ignorer (SS §6).
    private static bool NeedsCsrfToken(ApiDescription route)
    {
        var method = route.HttpMethod ?? string.Empty;
        var isWrite = !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method) && !HttpMethods.IsTrace(method);

        return isWrite && !route.ActionDescriptor.EndpointMetadata.OfType<IgnoreAntiforgeryTokenAttribute>().Any();
    }

    // Range le schéma de ce type parmi les composants du document, une seule fois, et renvoie un lien vers lui.
    private static async Task<IOpenApiSchema> ReferenceSchemaAsync(
        OpenApiOperationTransformerContext context,
        Type type,
        string name,
        CancellationToken cancellationToken)
    {
        var document = context.Document ?? throw new InvalidOperationException("Le document OpenAPI n'est pas encore créé.");

        if (document.Components?.Schemas?.ContainsKey(name) != true)
        {
            document.AddComponent(name, await context.GetOrCreateSchemaAsync(type, null, cancellationToken));
        }

        return new OpenApiSchemaReference(name, document);
    }

    // Une réponse réussie n'est écrite qu'en JSON : les autres formats proposés par défaut n'existent pas dans cette API.
    private static void KeepOnlyJson(OpenApiResponse response)
    {
        if (response.Content == null || !response.Content.TryGetValue(JsonMediaType, out var json))
        {
            return;
        }

        response.Content = new Dictionary<string, OpenApiMediaType> { [JsonMediaType] = json };
    }

    // Décrit l'en-tête Retry-After : toujours présent sur un 429 (voir RetryDelays), et sur un 503 quand le serveur est très demandé.
    private static void AddRetryDelay(OpenApiResponse response, int statusCode)
    {
        if (statusCode != StatusCodes.Status429TooManyRequests && statusCode != StatusCodes.Status503ServiceUnavailable)
        {
            return;
        }

        var isTooManyRequests = statusCode == StatusCodes.Status429TooManyRequests;
        response.Headers = new Dictionary<string, IOpenApiHeader>
        {
            ["Retry-After"] = new OpenApiHeader
            {
                Description = isTooManyRequests
                    ? "Délai en secondes avant de réessayer."
                    : "Délai en secondes avant de réessayer, présent seulement quand le serveur est très demandé.",
                Required = isTooManyRequests,
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1" }
            }
        };
    }

    // Donne la description en français d'un code, quand l'action n'en donne pas une plus précise.
    private static string Describe(int statusCode, ApiDescription route)
    {
        switch (statusCode)
        {
            case StatusCodes.Status200OK:
                return "La demande a réussi.";
            case StatusCodes.Status201Created:
                return "La ressource est créée.";
            case StatusCodes.Status204NoContent:
                return "La demande a réussi. La réponse n'a pas de contenu.";
            case StatusCodes.Status400BadRequest:
                return DescribeRefusedRequest(route);
            case StatusCodes.Status401Unauthorized:
                return "Connecte-toi pour continuer : la session manque, a expiré, ou son compte n'existe plus.";
            case StatusCodes.Status403Forbidden:
                return "Cette action est réservée aux administrateurs qui ont activé la double authentification.";
            case StatusCodes.Status404NotFound:
                return "Cette ressource n'existe pas, ou elle appartient à un autre compte.";
            case StatusCodes.Status409Conflict:
                return "La demande n'est pas possible dans l'état actuel : le titre de la réponse dit pourquoi.";
            case StatusCodes.Status413PayloadTooLarge:
                return "La requête est trop volumineuse.";
            case StatusCodes.Status415UnsupportedMediaType:
                return "Le corps de la requête doit être écrit en JSON (application/json).";
            case StatusCodes.Status429TooManyRequests:
                return "Trop de demandes : réessaie après le délai de l'en-tête Retry-After.";
            case StatusCodes.Status500InternalServerError:
                return "Une erreur inattendue est survenue. Réessaie plus tard.";
            case StatusCodes.Status503ServiceUnavailable:
                return "Le service est momentanément indisponible. Réessaie plus tard.";
            default:
                return ReasonPhrases.GetReasonPhrase(statusCode);
        }
    }

    // Décrit le refus d'une requête, avec les clés que cette route peut donner dans « errors » (voir ApiErrorConfiguration).
    private static string DescribeRefusedRequest(ApiDescription route)
    {
        var description = "La requête n'est pas valide. « errors » donne les messages de chaque champ refusé, sous son nom JSON, "
            + "et la clé « " + ApiErrorConfiguration.GeneralErrorsKey + " » ceux d'une requête illisible.";

        if (NeedsCsrfToken(route))
        {
            description += " La clé « csrfToken » dit que le jeton anti-CSRF manque ou n'est pas valide : demande un nouveau jeton, "
                + "puis réessaie.";
        }

        return description;
    }
}
