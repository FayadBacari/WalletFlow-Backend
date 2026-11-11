using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Filters;
using WalletFlow.Application.UserSessions;

namespace WalletFlow.Api.Controllers;

// Connexion d'un visiteur (GR Auth). Cette route est appelée sans session : elle n'a donc
// ni besoin d'être connecté, ni besoin du jeton anti-CSRF (SS §6).
[ApiController]
[Route("api/account")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class AccountLoginController : ControllerBase
{
    public const string LoggedInMessage = "Tu es connecté.";
    public const string CodeRequiredMessage = "Saisis le code de ton application d'authentification, ou un code de secours.";

    private readonly AccountLoginService loginService;

    // Reçoit le cas d'utilisation de la connexion.
    public AccountLoginController(AccountLoginService loginService)
    {
        this.loginService = loginService;
    }

    // POST api/account/login : ouvre une session avec l'adresse e-mail et le mot de passe.
    // Si la double authentification est activée, aucune session ne s'ouvre : la réponse demande le code.
    [HttpPost("login")]
    [ServiceFilter(typeof(MinimumAnswerDurationFilter))]
    [EnableRateLimiting(RateLimitingConfiguration.LoginPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> LogIn(LoginRequest request)
    {
        var result = await loginService.LogInAsync(new LoginAttempt
        {
            EmailAddress = request.Email,
            Password = request.Password,
            IpAddress = HttpContext.Connection.RemoteIpAddress,
            DeviceToken = Request.Cookies[SessionCookiesConfiguration.DeviceCookieName],
            SessionToken = Request.Cookies[SessionCookiesConfiguration.SessionCookieName]
        });

        if (result is AccountLoginResult.LoggedIn loggedIn)
        {
            SessionCookiesConfiguration.WriteLoginCookies(Response, loggedIn.SessionToken, loggedIn.DeviceToken);
            return Ok(new LoginResponse(LoggedInMessage, false));
        }

        if (result is AccountLoginResult.WaitingForTwoFactorCode waitingForCode)
        {
            Response.Cookies.Append(SessionCookiesConfiguration.WaitingLoginCookieName, waitingForCode.WaitingLoginToken, SessionCookiesConfiguration.CreateWaitingLoginCookieOptions());
            return Ok(new LoginResponse(CodeRequiredMessage, true));
        }

        if (result is AccountLoginResult.EmailAddressNotConfirmed)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: ErrorMessages.EmailNotConfirmed);
        }

        return Problem(statusCode: StatusCodes.Status401Unauthorized, title: ErrorMessages.WrongCredentials);
    }
}
