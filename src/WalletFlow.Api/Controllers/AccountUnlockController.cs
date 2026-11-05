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
    public const string MessageSentWhenTheAccountIsUnlocked = "Ton compte est débloqué. Tu peux de nouveau te connecter.";

    private readonly AccountUnlockService serviceThatUnlocksTheUserAccounts;

    // Reçoit le cas d'utilisation du déblocage.
    public AccountUnlockController(AccountUnlockService serviceThatUnlocksTheUserAccounts)
    {
        this.serviceThatUnlocksTheUserAccounts = serviceThatUnlocksTheUserAccounts;
    }

    // POST api/account/unlock : débloque le compte, avec le jeton du lien. Ne connecte personne.
    [HttpPost("unlock")]
    [EnableRateLimiting(RateLimitingConfiguration.PolicyNameForAccountUnlockAttemptsPerIpAddress)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumNumberOfBytesInOneAccountRequest)]
    public async Task<IActionResult> UnlockAccountFromTheVisitorRequest(UnlockAccountRequest requestSentByTheVisitorToUnlock)
    {
        var resultOfTheAccountUnlockRequest = await serviceThatUnlocksTheUserAccounts.UnlockAccountWithLinkTokenAsync(
            requestSentByTheVisitorToUnlock.Token,
            HttpContext.Connection.RemoteIpAddress);

        if (resultOfTheAccountUnlockRequest.IsUnlocked)
        {
            return Ok(new MessageResponse(MessageSentWhenTheAccountIsUnlocked));
        }

        ModelState.AddModelError(nameof(UnlockAccountRequest.Token), resultOfTheAccountUnlockRequest.LinkErrorMessage!);
        return ValidationProblem();
    }
}
