using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.AccountSettings;

namespace WalletFlow.Api.Controllers;

// Confirmation d'une nouvelle adresse e-mail (GR Compte). Le lien s'ouvre souvent depuis la boîte mail, sans session :
// cette route n'a donc ni besoin d'être connecté, ni besoin du jeton anti-CSRF (SS §6). Le mot de passe y prouve le compte.
[ApiController]
[Route("api/account")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class EmailChangeConfirmationController : ControllerBase
{
    public const string EmailChangedMessage = "Ton adresse e-mail est changée. Utilise-la désormais pour te connecter.";

    private readonly EmailChangeConfirmationService confirmationService;

    // Reçoit le cas d'utilisation de la confirmation.
    public EmailChangeConfirmationController(EmailChangeConfirmationService confirmationService)
    {
        this.confirmationService = confirmationService;
    }

    // POST api/account/confirm-email-change : le jeton du lien et le mot de passe confirment la nouvelle adresse.
    [HttpPost("confirm-email-change")]
    [EnableRateLimiting(RateLimitingConfiguration.EmailChangeLinkPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> ConfirmEmailChange(ConfirmEmailChangeRequest request)
    {
        var result = await confirmationService.ConfirmAsync(
            request.Token,
            request.Password,
            HttpContext.Connection.RemoteIpAddress);

        if (result is EmailChangeConfirmationResult.EmailChanged)
        {
            return Ok(new MessageResponse(EmailChangedMessage));
        }

        if (result is EmailChangeConfirmationResult.WrongPassword)
        {
            ModelState.AddModelError(nameof(ConfirmEmailChangeRequest.Password), ErrorMessages.WrongCurrentPassword);
            return ValidationProblem();
        }

        if (result is EmailChangeConfirmationResult.EmailAlreadyUsed)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.EmailAlreadyUsed);
        }

        ModelState.AddModelError(nameof(ConfirmEmailChangeRequest.Token), ErrorMessages.EmailChangeLinkNoLongerValid);
        return ValidationProblem();
    }
}
