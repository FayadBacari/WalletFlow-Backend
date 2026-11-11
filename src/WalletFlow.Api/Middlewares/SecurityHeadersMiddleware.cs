namespace WalletFlow.Api.Middlewares;

// Ajoute à chaque réponse les en-têtes de sécurité du §6 de la stratégie de sécurité.
// L'en-tête HSTS n'est pas ici : UseHsts() s'en charge dans Program.cs.
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate next;

    // Reçoit le middleware suivant, à appeler une fois les en-têtes prévus.
    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    // Appelée par ASP.NET Core pour chaque requête (nom imposé par le framework).
    public async Task InvokeAsync(HttpContext context)
    {
        // Les en-têtes sont écrits juste avant l'envoi de la réponse,
        // pour qu'ils soient présents même sur une réponse d'erreur.
        context.Response.OnStarting(() =>
        {
            AddSecurityHeaders(context.Response);
            return Task.CompletedTask;
        });

        await next(context);
    }

    // Écrit les quatre en-têtes de sécurité dans la réponse.
    private static void AddSecurityHeaders(HttpResponse response)
    {
        response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    }
}
