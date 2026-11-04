using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.UserSessions;

namespace WalletFlow.Api.Controllers;

// Routes d'un utilisateur connecté : le jeton anti-CSRF et la déconnexion (GR Auth, SS §3 et §6).
// Elles n'ont aucun attribut de sécurité : toute route demande par défaut une session et ce jeton (voir Program.cs).
[ApiController]
[Route("api/account")]
public class UserSessionController : ControllerBase
{
    public const string MessageSentWhenTheUserIsLoggedOut = "Tu es déconnecté.";

    private readonly UserSessionService serviceThatClosesTheUserSessions;
    private readonly IAntiforgery toolThatCreatesTheCsrfTokens;

    // Reçoit le cas d'utilisation des sessions et l'outil anti-CSRF d'ASP.NET Core.
    public UserSessionController(UserSessionService serviceThatClosesTheUserSessions, IAntiforgery toolThatCreatesTheCsrfTokens)
    {
        this.serviceThatClosesTheUserSessions = serviceThatClosesTheUserSessions;
        this.toolThatCreatesTheCsrfTokens = toolThatCreatesTheCsrfTokens;
    }

    // GET api/account/csrf-token : donne le jeton à joindre aux requêtes qui modifient des données.
    // Il est lié à l'utilisateur : il se demande donc une fois connecté. Il répond 401 sans session.
    [HttpGet("csrf-token")]
    public IActionResult GiveCsrfTokenToTheLoggedInUser()
    {
        var tokensCreatedForTheLoggedInUser = toolThatCreatesTheCsrfTokens.GetAndStoreTokens(HttpContext);

        return Ok(new CsrfTokenResponse(tokensCreatedForTheLoggedInUser.RequestToken!));
    }

    // POST api/account/logout : détruit la session côté serveur, puis le cookie (SS §3).
    // Le cookie d'appareil reste : le navigateur reste reconnu à la prochaine connexion.
    [HttpPost("logout")]
    public async Task<IActionResult> LogOutTheLoggedInUser()
    {
        await serviceThatClosesTheUserSessions.CloseUserSessionOfThisCookieAsync(Request.Cookies[SessionCookiesConfiguration.NameOfTheUserSessionCookie]!);

        Response.Cookies.Delete(SessionCookiesConfiguration.NameOfTheUserSessionCookie, SessionCookiesConfiguration.CreateOptionsOfTheUserSessionCookie());

        return Ok(new MessageResponse(MessageSentWhenTheUserIsLoggedOut));
    }
}
