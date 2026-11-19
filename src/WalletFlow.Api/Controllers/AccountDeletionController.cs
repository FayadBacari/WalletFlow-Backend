using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.AccountDeletion;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Api.Controllers;

// Demande de suppression du compte par l'utilisateur connecté (GR Suppression, SS §2, §6 et §7).
// Cette route n'a aucun attribut de sécurité : toute route demande par défaut une session et le jeton anti-CSRF (voir Program.cs).
[ApiController]
[Route("api/account")]
public class AccountDeletionController : ControllerBase
{
    public const string DeletionRequestedMessage =
        "La suppression de ton compte est demandée, et toutes tes sessions sont fermées. Pour changer d'avis, reconnecte-toi avant le jour de l'effacement.";

    private readonly AccountDeletionService deletionService;

    // Reçoit le cas d'utilisation de la demande de suppression.
    public AccountDeletionController(AccountDeletionService deletionService)
    {
        this.deletionService = deletionService;
    }

    // POST api/account/delete : le mot de passe, et le code si la double authentification est activée, demandent la suppression
    // du compte. La session en cours est fermée avec les autres : son cookie est effacé. La réponse donne le jour de Paris où le
    // compte sera effacé.
    [HttpPost("delete")]
    [EnableRateLimiting(RateLimitingConfiguration.AccountDeletionPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    [ProducesResponseType<AccountDeletionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.ServerBusyDescription)]
    public async Task<IActionResult> RequestDeletion(AccountDeletionRequest request, CancellationToken cancellationToken = default)
    {
        var result = await deletionService.RequestAsync(new AccountDeletionAttempt
        {
            UserId = SessionClaims.ReadUserId(User),
            Password = request.Password,
            Code = request.Code,
            IpAddress = HttpContext.Connection.RemoteIpAddress
        },
        cancellationToken);

        if (result is AccountDeletionResult.Requested requested)
        {
            Response.Cookies.Delete(SessionCookiesConfiguration.SessionCookieName, SessionCookiesConfiguration.CreateSessionCookieOptions());
            var erasureDay = DateOnly.FromDateTime(UsersCalendar.ConvertToUsersTime(requested.ErasesAt));
            return Ok(new AccountDeletionResponse(DeletionRequestedMessage, erasureDay));
        }

        if (result is AccountDeletionResult.AccountGone)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: UserSessionAuthenticationHandler.LoginRequiredMessage);
        }

        if (result is AccountDeletionResult.WrongPassword)
        {
            ModelState.AddModelError(nameof(AccountDeletionRequest.Password), ErrorMessages.WrongCurrentPassword);
            return ValidationProblem();
        }

        // Le même message pour les deux champs : l'erreur ne dit pas lequel est faux.
        ModelState.AddModelError(ApiErrorConfiguration.GeneralErrorsKey, ErrorMessages.WrongPasswordOrCode);
        return ValidationProblem();
    }
}
