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
    public const string MessageSentWhenTheLoginWaitsForItsCode = "Saisis le code de ton application d'authentification, ou un code de secours.";

    private readonly AccountLoginService serviceThatLogsTheVisitorsIn;

    // Reçoit le cas d'utilisation de la connexion.
    public AccountLoginController(AccountLoginService serviceThatLogsTheVisitorsIn)
    {
        this.serviceThatLogsTheVisitorsIn = serviceThatLogsTheVisitorsIn;
    }

    // POST api/account/login : ouvre une session avec l'adresse e-mail et le mot de passe.
    // Si la double authentification est activée, aucune session ne s'ouvre : la réponse demande le code.
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
            SessionCookiesConfiguration.WriteLoginCookiesInThisAnswer(Response, resultOfTheLoginOfTheVisitor.SessionTokenToPutInTheCookie!, resultOfTheLoginOfTheVisitor.DeviceTokenToPutInTheCookie!);
            return Ok(new LoginResponse(MessageSentWhenTheVisitorIsLoggedIn, false));
        }

        if (resultOfTheLoginOfTheVisitor.IsWaitingForTwoFactorCode)
        {
            Response.Cookies.Append(SessionCookiesConfiguration.NameOfTheWaitingLoginCookie, resultOfTheLoginOfTheVisitor.WaitingLoginTokenToPutInTheCookie!, SessionCookiesConfiguration.CreateOptionsOfTheWaitingLoginCookie());
            return Ok(new LoginResponse(MessageSentWhenTheLoginWaitsForItsCode, true));
        }

        if (resultOfTheLoginOfTheVisitor.EmailAddressNotConfirmedErrorMessage != null)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: resultOfTheLoginOfTheVisitor.EmailAddressNotConfirmedErrorMessage);
        }

        return Problem(statusCode: StatusCodes.Status401Unauthorized, title: resultOfTheLoginOfTheVisitor.WrongCredentialsErrorMessage);
    }
}
