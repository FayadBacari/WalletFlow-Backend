using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.AccountRecovery;

namespace WalletFlow.Api.Controllers;

// Déblocage d'un compte avec le lien reçu par e-mail (GR Auth). Cette route est appelée sans session :
// elle n'a donc ni besoin d'être connecté, ni besoin du jeton anti-CSRF (SS §6).
[ApiController]
[Route("api/account")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class AccountUnlockController : ControllerBase
{
    public const string UnlockedMessage = "Ton compte est débloqué. Tu peux de nouveau te connecter.";

    private readonly AccountUnlockService unlockService;

    // Reçoit le cas d'utilisation du déblocage.
    public AccountUnlockController(AccountUnlockService unlockService)
    {
        this.unlockService = unlockService;
    }

    // POST api/account/unlock : débloque le compte, avec le jeton du lien. Ne connecte personne.
    [HttpPost("unlock")]
    [EnableRateLimiting(RateLimitingConfiguration.AccountUnlockPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> Unlock(UnlockAccountRequest request)
    {
        var result = await unlockService.UnlockAsync(
            request.Token,
            HttpContext.Connection.RemoteIpAddress);

        if (result is AccountUnlockResult.AccountUnlocked)
        {
            return Ok(new MessageResponse(UnlockedMessage));
        }

        ModelState.AddModelError(nameof(UnlockAccountRequest.Token), ErrorMessages.UnlockLinkNoLongerValid);
        return ValidationProblem();
    }
}
