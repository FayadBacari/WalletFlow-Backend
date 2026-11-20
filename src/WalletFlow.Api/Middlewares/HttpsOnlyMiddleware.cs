using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using WalletFlow.Api.Configuration;

namespace WalletFlow.Api.Middlewares;

// Les routes de l'API ne répondent qu'en HTTPS (SS §7). Une requête en HTTP simple est d'abord redirigée vers HTTPS ; sans port
// HTTPS connu, la redirection ne peut pas se faire, et cette étape refuse la requête, avant les limites, la session et la base.
// Sans elle, une écriture en HTTP répondait 500, car le cookie du jeton anti-CSRF exige HTTPS, et une lecture avec un cookie de
// session répondait en clair. Les points de santé restent ouverts en HTTP : l'hébergeur les sonde depuis son réseau privé.
public class HttpsOnlyMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<HttpsOnlyMiddleware> logger;

    // Reçoit l'étape suivante de la chaîne et le journal technique.
    public HttpsOnlyMiddleware(RequestDelegate next, ILogger<HttpsOnlyMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    // Laisse passer une requête en HTTPS, ou qui ne vise aucune route de l'API. Refuse les autres, avec un avertissement au
    // journal : derrière le proxy inverse, il signale un proxy qui n'envoie pas X-Forwarded-Proto, ou qui n'est pas déclaré.
    public async Task InvokeAsync(HttpContext context)
    {
        var isApiRoute = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() != null;

        if (context.Request.IsHttps || !isApiRoute)
        {
            await next(context);
            return;
        }

        logger.LogWarning(
            "Une requête vers l'API est arrivée en HTTP simple : elle est refusée. Si elle vient du proxy inverse, vérifie qu'il "
            + "envoie X-Forwarded-Proto et qu'il est déclaré dans « " + ReverseProxyConfiguration.SectionName + " ».");

        var answer = ApiErrorConfiguration.CreateHttpsRefusalAnswer(context);
        await answer.ExecuteResultAsync(new ActionContext(context, context.GetRouteData(), new ActionDescriptor()));
    }
}
