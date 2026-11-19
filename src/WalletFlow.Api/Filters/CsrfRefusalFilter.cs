using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using WalletFlow.Api.Configuration;

namespace WalletFlow.Api.Filters;

// Donne un message clair au refus d'une écriture sans jeton anti-CSRF valable (SS §6). ASP.NET Core n'y répond qu'un 400 sans
// raison : le site ne saurait pas qu'il lui suffit de demander un nouveau jeton. Le filtre ne lit ni le compte ni la base, et
// n'écrit rien au journal : il change seulement le texte du refus.
public class CsrfRefusalFilter : IAlwaysRunResultFilter, IOrderedFilter
{
    // Rang du filtre : avant celui d'ASP.NET Core qui change tout 400 sans corps en erreur sans raison, et qui a le rang -2000.
    // Après lui, le refus ne serait plus reconnaissable.
    public const int OrderBeforeClientErrors = -3000;

    public int Order => OrderBeforeClientErrors;

    // Appelée par ASP.NET Core avant l'écriture de toute réponse, même celle d'un filtre qui refuse la requête avant l'action
    // (nom imposé par le framework). Toute autre réponse passe sans changement.
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is IAntiforgeryValidationFailedResult)
        {
            context.Result = ApiErrorConfiguration.CreateCsrfRefusalAnswer(context.HttpContext);
        }
    }

    // Appelée par ASP.NET Core après l'écriture de la réponse (nom imposé par le framework) : il n'y a plus rien à faire.
    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
