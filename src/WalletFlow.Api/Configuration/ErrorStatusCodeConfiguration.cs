using Microsoft.AspNetCore.Diagnostics;

namespace WalletFlow.Api.Configuration;

// Choisit le code de réponse d'une erreur attrapée en production.
public static class ErrorStatusCodeConfiguration
{
    // Une requête refusée par le serveur lui-même, par exemple trop grosse (413), garde son vrai code :
    // ce n'est pas une panne. Toute autre erreur reste une erreur 500 générique (SS §10).
    public static int ChooseStatusCode(Exception exception)
    {
        if (exception is BadHttpRequestException badRequest)
        {
            return badRequest.StatusCode;
        }

        return StatusCodes.Status500InternalServerError;
    }

    // Une requête refusée par le serveur lui-même n'est pas écrite au journal comme une panne : sinon, n'importe qui pourrait
    // remplir le journal d'erreurs, et faire sonner la surveillance, à coups de corps trop gros. Toute autre erreur y est
    // écrite, avec son détail.
    public static bool IsRefusedRequest(ExceptionHandlerSuppressDiagnosticsContext context)
    {
        return context.Exception is BadHttpRequestException;
    }
}
