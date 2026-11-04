namespace WalletFlow.Api.Configuration;

// Choisit le code de réponse d'une erreur attrapée en production.
public static class ErrorStatusCodeConfiguration
{
    // Une requête refusée par le serveur lui-même, par exemple trop grosse (413), garde son vrai code :
    // ce n'est pas une panne. Toute autre erreur reste une erreur 500 générique (SS §10).
    public static int ChooseStatusCodeForTheCaughtException(Exception exceptionCaughtByTheErrorHandler)
    {
        if (exceptionCaughtByTheErrorHandler is BadHttpRequestException requestRefusedByTheWebServer)
        {
            return requestRefusedByTheWebServer.StatusCode;
        }

        return StatusCodes.Status500InternalServerError;
    }
}
