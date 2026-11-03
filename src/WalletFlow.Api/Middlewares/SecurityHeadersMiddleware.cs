namespace WalletFlow.Api.Middlewares;

// Ajoute à chaque réponse les en-têtes de sécurité du §6 de la stratégie de sécurité.
// L'en-tête HSTS n'est pas ici : UseHsts() s'en charge dans Program.cs.
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate nextMiddlewareInTheRequestPipeline;

    // Reçoit le middleware suivant, à appeler une fois les en-têtes prévus.
    public SecurityHeadersMiddleware(RequestDelegate nextMiddlewareInTheRequestPipeline)
    {
        this.nextMiddlewareInTheRequestPipeline = nextMiddlewareInTheRequestPipeline;
    }

    // Appelée par ASP.NET Core pour chaque requête (nom imposé par le framework).
    public async Task InvokeAsync(HttpContext contextOfTheCurrentHttpRequest)
    {
        // Les en-têtes sont écrits juste avant l'envoi de la réponse,
        // pour qu'ils soient présents même sur une réponse d'erreur.
        contextOfTheCurrentHttpRequest.Response.OnStarting(() =>
        {
            AddSecurityHeadersToTheHttpResponse(contextOfTheCurrentHttpRequest.Response);
            return Task.CompletedTask;
        });

        await nextMiddlewareInTheRequestPipeline(contextOfTheCurrentHttpRequest);
    }

    // Écrit les quatre en-têtes de sécurité dans la réponse.
    private static void AddSecurityHeadersToTheHttpResponse(HttpResponse responseSentBackToTheClientApplication)
    {
        responseSentBackToTheClientApplication.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        responseSentBackToTheClientApplication.Headers["X-Content-Type-Options"] = "nosniff";
        responseSentBackToTheClientApplication.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        responseSentBackToTheClientApplication.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    }
}
