using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.AccountSettings;

namespace WalletFlow.Api.Controllers;

// Demande de changement d'adresse e-mail par l'utilisateur connecté (GR Compte, SS §2).
// Cette route n'a aucun attribut de sécurité : toute route demande par défaut une session et le jeton anti-CSRF (voir Program.cs).
[ApiController]
[Route("api/account")]
public class EmailChangeController : ControllerBase
{
    // Le même message que la nouvelle adresse soit libre ou déjà utilisée (SS §2).
    public const string RequestAcceptedMessage =
        "Un e-mail vient d'être envoyé à cette adresse. Suis ses instructions pour finir le changement.";

    private readonly EmailChangeRequestService emailChangeRequestService;

    // Reçoit le cas d'utilisation de la demande de changement d'adresse.
    public EmailChangeController(EmailChangeRequestService emailChangeRequestService)
    {
        this.emailChangeRequestService = emailChangeRequestService;
    }

    // POST api/account/email : le mot de passe, et le code si la double authentification est activée, demandent une nouvelle adresse.
    // L'adresse ne change qu'une fois le lien ouvert et le mot de passe saisi de nouveau (voir EmailChangeConfirmationController).
    [HttpPost("email")]
    [EnableRateLimiting(RateLimitingConfiguration.EmailChangePolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> RequestEmailChange(ChangeEmailRequest request)
    {
        var result = await emailChangeRequestService.RequestAsync(new EmailChangeAttempt
        {
            UserId = SessionClaims.ReadUserId(User),
            Password = request.Password,
            NewEmail = request.NewEmail,
            Code = request.Code,
            IpAddress = HttpContext.Connection.RemoteIpAddress
        });

        if (result is EmailChangeRequestResult.RequestAccepted)
        {
            return Ok(new MessageResponse(RequestAcceptedMessage));
        }

        if (result is EmailChangeRequestResult.TooManyRequests)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return AnswerRefusal(result);
    }

    // Donne le message de chaque refus. Avec la double authentification, un mauvais mot de passe et un mauvais code
    // reçoivent le même message, qui ne dit pas lequel est faux.
    private ActionResult AnswerRefusal(EmailChangeRequestResult result)
    {
        if (result is EmailChangeRequestResult.InvalidEmailAddress)
        {
            ModelState.AddModelError(nameof(ChangeEmailRequest.NewEmail), ErrorMessages.InvalidEmailAddress);
        }
        else if (result is EmailChangeRequestResult.SameEmailAddress)
        {
            ModelState.AddModelError(nameof(ChangeEmailRequest.NewEmail), ErrorMessages.SameEmailAddress);
        }
        else if (result is EmailChangeRequestResult.WrongPassword)
        {
            ModelState.AddModelError(nameof(ChangeEmailRequest.Password), ErrorMessages.WrongCurrentPassword);
        }
        else
        {
            ModelState.AddModelError(ApiErrorConfiguration.GeneralErrorsKey, ErrorMessages.WrongPasswordOrCode);
        }

        return ValidationProblem();
    }
}
