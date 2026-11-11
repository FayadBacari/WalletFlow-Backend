using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Api.Controllers;

// Fin de la connexion avec un code (GR Auth). Cette route est appelée sans session : elle n'a donc
// ni besoin d'être connecté, ni besoin du jeton anti-CSRF (SS §6).
[ApiController]
[Route("api/account")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class TwoFactorLoginController : ControllerBase
{
    private readonly TwoFactorLoginService twoFactorLoginService;

    // Reçoit le cas d'utilisation de la fin de la connexion.
    public TwoFactorLoginController(TwoFactorLoginService twoFactorLoginService)
    {
        this.twoFactorLoginService = twoFactorLoginService;
    }

    // POST api/account/login/two-factor : ouvre la session de la connexion qui attend son code.
    // Un mauvais code répond 400 : le site reste sur le formulaire du code. 401 veut dire qu'il faut revenir au mot de passe.
    [HttpPost("login/two-factor")]
    [EnableRateLimiting(RateLimitingConfiguration.TwoFactorCodePolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> FinishLogin(TwoFactorCodeRequest request)
    {
        var result = await twoFactorLoginService.FinishAsync(new TwoFactorCodeAttempt
        {
            Code = request.Code,
            IpAddress = HttpContext.Connection.RemoteIpAddress,
            WaitingLoginToken = Request.Cookies[SessionCookiesConfiguration.WaitingLoginCookieName],
            DeviceToken = Request.Cookies[SessionCookiesConfiguration.DeviceCookieName],
            SessionToken = Request.Cookies[SessionCookiesConfiguration.SessionCookieName]
        });

        if (result is TwoFactorLoginResult.WrongCode)
        {
            ModelState.AddModelError(nameof(TwoFactorCodeRequest.Code), ErrorMessages.WrongTwoFactorCode);
            return ValidationProblem();
        }

        Response.Cookies.Delete(SessionCookiesConfiguration.WaitingLoginCookieName, SessionCookiesConfiguration.CreateWaitingLoginCookieOptions());

        if (result is TwoFactorLoginResult.LoggedIn loggedIn)
        {
            SessionCookiesConfiguration.WriteLoginCookies(Response, loggedIn.SessionToken, loggedIn.DeviceToken);
            return Ok(new LoginResponse(AccountLoginController.LoggedInMessage, false));
        }

        return Problem(statusCode: StatusCodes.Status401Unauthorized, title: ErrorMessages.LoginMustRestart);
    }
}
