using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Administration;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Api.Filters;

// Réserve une route à l'administrateur (GR Admin, SS §4 et §8). Il passe avant la lecture de la requête, après la session :
// une requête sans session est déjà refusée (401) par la règle commune à toutes les routes (voir Program.cs). Il passe aussi
// après le jeton anti-CSRF : une écriture sans jeton est refusée avant de lire le compte. Le rôle et la double
// authentification sont relus en base à chaque requête. Un utilisateur qui n'est pas administrateur reçoit « interdit » (403),
// et son essai est noté au journal comme un accès refusé (SS §11). Un administrateur sans double authentification reçoit aussi
// 403, avec ce qu'il doit faire : ce n'est pas une tentative d'accès, rien n'est noté.
public class AdministratorOnlyFilter : IAsyncAuthorizationFilter
{
    // Rang du filtre : juste après la vérification du jeton anti-CSRF d'ASP.NET Core, qui a le rang 1000. Avec le rang par
    // défaut, 0, le rôle passait avant : un site qui faisait envoyer une écriture par le navigateur d'un utilisateur remplissait
    // son journal d'accès refusés.
    public const int OrderAfterCsrfCheck = 1001;

    private readonly AdministratorAccessService accessService;
    private readonly SecurityJournalService securityJournal;
    private readonly ProblemDetailsFactory problemDetailsFactory;

    // Reçoit la vérification de l'administrateur, le journal de sécurité et le format des erreurs.
    public AdministratorOnlyFilter(
        AdministratorAccessService accessService,
        SecurityJournalService securityJournal,
        ProblemDetailsFactory problemDetailsFactory)
    {
        this.accessService = accessService;
        this.securityJournal = securityJournal;
        this.problemDetailsFactory = problemDetailsFactory;
    }

    // Laisse passer l'administrateur, et répond 403 à tout autre utilisateur (nom imposé par le framework).
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var userId = SessionClaims.ReadUserId(context.HttpContext.User);
        var access = await accessService.CheckAsync(userId, context.HttpContext.RequestAborted);

        if (access == AdministratorAccess.Granted)
        {
            return;
        }

        if (access == AdministratorAccess.NotAdministrator)
        {
            await securityJournal.SaveEventNowAsync(
                SecurityEventType.AccessDenied,
                SecurityEventResult.Failure,
                userId,
                context.HttpContext.Connection.RemoteIpAddress);
            context.Result = CreateForbiddenAnswer(context.HttpContext, ErrorMessages.AdministratorOnly);
            return;
        }

        context.Result = CreateForbiddenAnswer(context.HttpContext, ErrorMessages.AdministratorTwoFactorRequired);
    }

    // Donne la réponse 403 avec ce titre, au même format que les autres erreurs.
    private ObjectResult CreateForbiddenAnswer(HttpContext httpContext, string title)
    {
        var problem = problemDetailsFactory.CreateProblemDetails(httpContext, StatusCodes.Status403Forbidden, title);

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
