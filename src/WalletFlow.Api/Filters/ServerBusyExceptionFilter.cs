using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Security;

namespace WalletFlow.Api.Filters;

// Transforme le refus d'un calcul coûteux en réponse « serveur occupé » (503), avec le délai après lequel réessayer : la requête
// n'a rien changé, et ce n'est pas une panne. Le même refus vaut pour toutes les adresses : il ne révèle aucun compte (SS §2).
public class ServerBusyExceptionFilter : IExceptionFilter
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly ProblemDetailsFactory problemDetailsFactory;

    // Reçoit la fabrique des réponses d'erreur, la même que pour les autres refus.
    public ServerBusyExceptionFilter(ProblemDetailsFactory problemDetailsFactory)
    {
        this.problemDetailsFactory = problemDetailsFactory;
    }

    // Appelée par ASP.NET Core quand une action lève une exception (nom imposé par le framework). Toute autre exception continue
    // son chemin.
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ServerBusyException)
        {
            return;
        }

        var problemDetails = problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            StatusCodes.Status503ServiceUnavailable,
            ErrorMessages.ServerBusy);

        context.HttpContext.Response.Headers.RetryAfter = RetryDelays.InSeconds(RetryDelay);
        context.Result = new ObjectResult(problemDetails)
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
            ContentTypes = { "application/problem+json" }
        };
        context.ExceptionHandled = true;
    }
}
