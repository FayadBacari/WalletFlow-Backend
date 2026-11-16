using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Api.Controllers;

// Le lien du support qui désactive la double authentification (GR Auth, SS §2). Il s'ouvre depuis la boîte mail, sans session :
// cette route n'a donc ni besoin d'être connecté, ni besoin du jeton anti-CSRF (SS §6). Le mot de passe y prouve le compte.
[ApiController]
[Route("api/account")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class TwoFactorSupportLinkController : ControllerBase
{
    public const string TwoFactorDisabledMessage =
        "La double authentification est désactivée, et toutes tes sessions sont fermées. Connecte-toi avec ton mot de passe, puis réactive-la.";

    private readonly TwoFactorSupportLinkService linkService;

    // Reçoit le cas d'utilisation du lien.
    public TwoFactorSupportLinkController(TwoFactorSupportLinkService linkService)
    {
        this.linkService = linkService;
    }

    // POST api/account/disable-two-factor : le jeton du lien et le mot de passe désactivent la double authentification.
    [HttpPost("disable-two-factor")]
    [EnableRateLimiting(RateLimitingConfiguration.TwoFactorSupportLinkPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> DisableTwoFactor(DisableTwoFactorRequest request)
    {
        var result = await linkService.DeactivateAsync(request.Token, request.Password, HttpContext.Connection.RemoteIpAddress);

        if (result is TwoFactorSupportLinkResult.Deactivated)
        {
            return Ok(new MessageResponse(TwoFactorDisabledMessage));
        }

        if (result is TwoFactorSupportLinkResult.WrongPassword)
        {
            ModelState.AddModelError(nameof(DisableTwoFactorRequest.Password), ErrorMessages.WrongCurrentPassword);
            return ValidationProblem();
        }

        var tokenError = result switch
        {
            TwoFactorSupportLinkResult.LinkCancelled => ErrorMessages.TwoFactorLinkCancelled,
            TwoFactorSupportLinkResult.PasswordChangedRecently => ErrorMessages.TwoFactorLinkAfterPasswordChange,
            _ => ErrorMessages.TwoFactorLinkNoLongerValid
        };

        ModelState.AddModelError(nameof(DisableTwoFactorRequest.Token), tokenError);
        return ValidationProblem();
    }
}
