using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace WalletFlow.Tests.Api;

// Vérifie, dans chaque test de l'API, que chaque réponse reçue est décrite dans OpenAPI : son code pour sa route, et le type de son
// corps. Une route qui gagne un refus sans le décrire fait échouer le premier test qui le provoque, avec un message qui le dit.
// La réponse est gardée en mémoire jusqu'à la fin de la requête : la vérification passe après tout, même après le gestionnaire
// des erreurs du mode production, qui ne peut donc pas cacher son échec.
public static class ResponseContractCheck
{
    private const string ViolationKey = "walletflow-tests:reponse-non-decrite";
    private static readonly SemaphoreSlim LoadingLock = new SemaphoreSlim(1, 1);
    private static Dictionary<string, HashSet<int>>? documentedCodes;

    // Ajoute la vérification à une API de test.
    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton<IStartupFilter, CheckStartupFilter>();
        services.Configure<MvcOptions>(options => options.Filters.Add(new BodyTypeFilter()));
    }

    // Donne la clé d'une opération, écrite comme dans le document : « POST /api/bags/{bagId} ».
    public static string ReadOperationKey(string method, string routeTemplate)
    {
        return method.ToUpperInvariant() + " /" + Regex.Replace(routeTemplate, ":[^}]+}", "}");
    }

    // Lit une fois les codes décrits pour chaque opération, dans le document que l'API génère elle-même.
    private static async Task<Dictionary<string, HashSet<int>>> ReadDocumentedCodesAsync(IServiceProvider services)
    {
        if (documentedCodes != null)
        {
            return documentedCodes;
        }

        await LoadingLock.WaitAsync();

        try
        {
            if (documentedCodes == null)
            {
                var document = await services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1").GetOpenApiDocumentAsync();
                var json = JsonDocument.Parse(await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1));
                var codes = new Dictionary<string, HashSet<int>>();

                foreach (var path in json.RootElement.GetProperty("paths").EnumerateObject())
                {
                    foreach (var operation in path.Value.EnumerateObject())
                    {
                        var responses = operation.Value.GetProperty("responses").EnumerateObject();
                        codes[operation.Name.ToUpperInvariant() + " " + path.Name] =
                            responses.Select(response => int.Parse(response.Name, CultureInfo.InvariantCulture)).ToHashSet();
                    }
                }

                documentedCodes = codes;
            }
        }
        finally
        {
            LoadingLock.Release();
        }

        return documentedCodes;
    }

    // Dit si une réponse est une erreur au format « problem details ».
    private static bool IsProblem(HttpResponse response)
    {
        return response.ContentType?.StartsWith("application/problem+json", StringComparison.Ordinal) == true;
    }

    // Dit si le corps JSON gardé en mémoire porte un traceId.
    private static bool HasTraceId(MemoryStream buffer)
    {
        using var body = JsonDocument.Parse(buffer.ToArray());

        return body.RootElement.ValueKind == JsonValueKind.Object && body.RootElement.TryGetProperty("traceId", out _);
    }

    // Garde la réponse en mémoire, puis la vérifie avant de l'envoyer.
    private sealed class CheckStartupFilter : IStartupFilter
    {
        // Place la vérification au tout début de la chaîne de l'API (nom imposé par le framework).
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(CheckAsync);
                next(app);
            };
        }

        // Laisse passer la requête, puis refuse d'envoyer une réponse que le document ne décrit pas.
        private static async Task CheckAsync(HttpContext context, Func<Task> nextMiddleware)
        {
            var realBody = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            try
            {
                await nextMiddleware();
            }
            finally
            {
                context.Response.Body = realBody;
            }

            if (context.Items.TryGetValue(ViolationKey, out var violation))
            {
                throw new InvalidOperationException((string)violation!);
            }

            // Sans action, la requête n'a touché aucune route décrite : une adresse inconnue, ou une erreur déjà effacée. La redirection
            // vers HTTPS n'est pas une réponse de l'API non plus : elle répond à toute requête en HTTP simple, comme le refus de cette
            // requête quand la redirection ne peut pas se faire (voir HttpsOnlyMiddleware).
            var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            var isAnswerToPlainHttp = !context.Request.IsHttps
                && context.Response.StatusCode is StatusCodes.Status307TemporaryRedirect
                    or StatusCodes.Status308PermanentRedirect
                    or StatusCodes.Status400BadRequest;

            if (action != null && !isAnswerToPlainHttp)
            {
                var key = ReadOperationKey(context.Request.Method, action.AttributeRouteInfo!.Template!);
                var codes = await ReadDocumentedCodesAsync(context.RequestServices);

                if (!codes.TryGetValue(key, out var documented) || !documented.Contains(context.Response.StatusCode))
                {
                    throw new InvalidOperationException(
                        "La réponse " + context.Response.StatusCode + " de " + key + " n'est pas décrite dans OpenAPI.");
                }
            }

            // Aucune erreur, sur aucune adresse, ne porte de traceId (voir ApiErrorConfiguration.PrepareProblem).
            if (IsProblem(context.Response) && HasTraceId(buffer))
            {
                throw new InvalidOperationException(
                    "La réponse " + context.Response.StatusCode + " de " + context.Request.Method + " " + context.Request.Path
                    + " porte un traceId.");
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(realBody);
        }
    }

    // Compare le corps d'une réponse au type que son action annonce pour ce code. Le filtre passe le dernier, après tous ceux qui
    // changent la réponse.
    private sealed class BodyTypeFilter : IAlwaysRunResultFilter, IOrderedFilter
    {
        public int Order => int.MaxValue;

        // Appelée avant l'écriture de la réponse (nom imposé par le framework).
        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is not ObjectResult result)
            {
                return;
            }

            var statusCode = result.StatusCode ?? StatusCodes.Status200OK;
            var bodyType = result.Value?.GetType();
            var expectedType = FindExpectedType(context, statusCode);

            if (expectedType == null || bodyType == null || !expectedType.IsAssignableFrom(bodyType))
            {
                var key = ReadOperationKey(context.HttpContext.Request.Method, context.ActionDescriptor.AttributeRouteInfo!.Template!);
                context.HttpContext.Items[ViolationKey] = "Le corps " + (bodyType?.Name ?? "vide") + " de la réponse " + statusCode
                    + " de " + key + " n'est pas le type décrit dans OpenAPI : " + (expectedType?.Name ?? "aucun") + ".";
            }
        }

        // Appelée après l'écriture de la réponse (nom imposé par le framework) : il n'y a plus rien à vérifier.
        public void OnResultExecuted(ResultExecutedContext context)
        {
        }

        // Donne le type attendu : celui que l'action annonce pour une réussite, et le format « problem details » pour une erreur.
        private static Type? FindExpectedType(ResultExecutingContext context, int statusCode)
        {
            if (statusCode == StatusCodes.Status400BadRequest)
            {
                return typeof(HttpValidationProblemDetails);
            }

            if (statusCode > StatusCodes.Status400BadRequest)
            {
                return typeof(ProblemDetails);
            }

            return context.ActionDescriptor.EndpointMetadata
                .OfType<IApiResponseMetadataProvider>()
                .FirstOrDefault(metadata => metadata.StatusCode == statusCode)?.Type;
        }
    }
}
