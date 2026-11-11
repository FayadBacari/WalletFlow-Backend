using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.UserAccounts;

namespace WalletFlow.Api.Controllers;

// Confirmation de l'adresse e-mail d'un nouveau compte (GR Auth). Cette route est appelée sans session :
// elle n'a donc ni besoin d'être connecté, ni besoin du jeton anti-CSRF (SS §6).
[ApiController]
[Route("api/account")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class AccountConfirmationController : ControllerBase
{
    public const string ConfirmedMessage = "Ton adresse e-mail est confirmée. Tu peux maintenant te connecter.";

    private readonly AccountConfirmationService confirmationService;

    // Reçoit le cas d'utilisation de la confirmation.
    public AccountConfirmationController(AccountConfirmationService confirmationService)
    {
        this.confirmationService = confirmationService;
    }

    // POST api/account/confirm-email : active le compte, avec le jeton du lien et le mot de passe.
    [HttpPost("confirm-email")]
    [EnableRateLimiting(RateLimitingConfiguration.ConfirmationPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> ConfirmAccount(ConfirmAccountRequest request)
    {
        var result = await confirmationService.ConfirmAsync(
            request.Token,
            request.Password);

        if (result == AccountConfirmationResult.AccountConfirmed)
        {
            return Ok(new MessageResponse(ConfirmedMessage));
        }

        if (result == AccountConfirmationResult.WrongPassword)
        {
            ModelState.AddModelError(nameof(ConfirmAccountRequest.Password), ErrorMessages.WrongConfirmationPassword);
            return ValidationProblem();
        }

        ModelState.AddModelError(nameof(ConfirmAccountRequest.Token), ErrorMessages.ConfirmationLinkNoLongerValid);
        return ValidationProblem();
    }
}
