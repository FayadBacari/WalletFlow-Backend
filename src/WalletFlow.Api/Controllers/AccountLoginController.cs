using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
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
    public const string MessageSentWhenTheVisitorIsLoggedIn = "Tu es connecté.";

    private readonly AccountLoginService serviceThatLogsTheVisitorsIn;

    // Reçoit le cas d'utilisation de la connexion.
    public AccountLoginController(AccountLoginService serviceThatLogsTheVisitorsIn)
    {
        this.serviceThatLogsTheVisitorsIn = serviceThatLogsTheVisitorsIn;
    }

    // POST api/account/login : ouvre une session avec l'adresse e-mail et le mot de passe.
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitingConfiguration.PolicyNameForLoginAttemptsPerIpAddress)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumNumberOfBytesInOneAccountRequest)]
    public async Task<IActionResult> LogVisitorInFromTheVisitorRequest(LoginRequest requestSentByTheVisitorToLogIn)
    {
        var resultOfTheLoginOfTheVisitor = await serviceThatLogsTheVisitorsIn.LogVisitorInWithEmailAddressAndPasswordAsync(new LoginAttemptFromVisitor
        {
            EmailAddressTypedByTheVisitor = requestSentByTheVisitorToLogIn.Email,
            PasswordTextTypedByTheVisitor = requestSentByTheVisitorToLogIn.Password,
            IpAddressOfTheVisitor = HttpContext.Connection.RemoteIpAddress,
            DeviceTokenReadInTheCookie = Request.Cookies[SessionCookiesConfiguration.NameOfTheDeviceTokenCookie],
            SessionTokenReadInTheCookie = Request.Cookies[SessionCookiesConfiguration.NameOfTheUserSessionCookie]
        });

        if (resultOfTheLoginOfTheVisitor.IsLoggedIn)
        {
            WriteLoginCookiesInTheAnswer(resultOfTheLoginOfTheVisitor);
            return Ok(new MessageResponse(MessageSentWhenTheVisitorIsLoggedIn));
        }

        if (resultOfTheLoginOfTheVisitor.EmailAddressNotConfirmedErrorMessage != null)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: resultOfTheLoginOfTheVisitor.EmailAddressNotConfirmedErrorMessage);
        }

        return Problem(statusCode: StatusCodes.Status401Unauthorized, title: resultOfTheLoginOfTheVisitor.WrongCredentialsErrorMessage);
    }

    // Écrit le cookie de session et le cookie d'appareil, renouvelé à chaque connexion.
    private void WriteLoginCookiesInTheAnswer(AccountLoginResult resultOfTheLoginOfTheVisitor)
    {
        Response.Cookies.Append(
            SessionCookiesConfiguration.NameOfTheUserSessionCookie,
            resultOfTheLoginOfTheVisitor.SessionTokenToPutInTheCookie!,
            SessionCookiesConfiguration.CreateOptionsOfTheUserSessionCookie());

        Response.Cookies.Append(
            SessionCookiesConfiguration.NameOfTheDeviceTokenCookie,
            resultOfTheLoginOfTheVisitor.DeviceTokenToPutInTheCookie!,
            SessionCookiesConfiguration.CreateOptionsOfTheDeviceTokenCookie());
    }
}
