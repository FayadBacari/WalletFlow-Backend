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
    private readonly TwoFactorLoginService serviceThatFinishesTheLoginsWithACode;

    // Reçoit le cas d'utilisation de la fin de la connexion.
    public TwoFactorLoginController(TwoFactorLoginService serviceThatFinishesTheLoginsWithACode)
    {
        this.serviceThatFinishesTheLoginsWithACode = serviceThatFinishesTheLoginsWithACode;
    }

    // POST api/account/login/two-factor : ouvre la session de la connexion qui attend son code.
    // Un mauvais code répond 400 : le site reste sur le formulaire du code. 401 veut dire qu'il faut revenir au mot de passe.
    [HttpPost("login/two-factor")]
    [EnableRateLimiting(RateLimitingConfiguration.PolicyNameForTwoFactorCodeAttemptsPerIpAddress)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumNumberOfBytesInOneAccountRequest)]
    public async Task<IActionResult> FinishLoginFromTheVisitorRequest(TwoFactorCodeRequest requestSentByTheVisitorWithItsCode)
    {
        var resultOfTheLoginOfTheVisitor = await serviceThatFinishesTheLoginsWithACode.FinishLoginOfTheVisitorWithThisCodeAsync(new TwoFactorCodeAttemptFromVisitor
        {
            CodeTypedByTheVisitor = requestSentByTheVisitorWithItsCode.Code,
            IpAddressOfTheVisitor = HttpContext.Connection.RemoteIpAddress,
            WaitingLoginTokenReadInTheCookie = Request.Cookies[SessionCookiesConfiguration.NameOfTheWaitingLoginCookie],
            DeviceTokenReadInTheCookie = Request.Cookies[SessionCookiesConfiguration.NameOfTheDeviceTokenCookie],
            SessionTokenReadInTheCookie = Request.Cookies[SessionCookiesConfiguration.NameOfTheUserSessionCookie]
        });

        if (resultOfTheLoginOfTheVisitor.WrongCodeErrorMessage != null)
        {
            ModelState.AddModelError(nameof(TwoFactorCodeRequest.Code), resultOfTheLoginOfTheVisitor.WrongCodeErrorMessage);
            return ValidationProblem();
        }

        Response.Cookies.Delete(SessionCookiesConfiguration.NameOfTheWaitingLoginCookie, SessionCookiesConfiguration.CreateOptionsOfTheWaitingLoginCookie());

        if (!resultOfTheLoginOfTheVisitor.IsLoggedIn)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: resultOfTheLoginOfTheVisitor.LoginMustRestartErrorMessage);
        }

        SessionCookiesConfiguration.WriteLoginCookiesInThisAnswer(Response, resultOfTheLoginOfTheVisitor.SessionTokenToPutInTheCookie!, resultOfTheLoginOfTheVisitor.DeviceTokenToPutInTheCookie!);
        return Ok(new LoginResponse(AccountLoginController.MessageSentWhenTheVisitorIsLoggedIn, false));
    }
}
