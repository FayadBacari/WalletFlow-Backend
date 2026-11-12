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
    public const string LoggedOutMessage = "Tu es déconnecté.";

    private readonly UserSessionService sessionService;
    private readonly IAntiforgery antiforgery;

    // Reçoit le cas d'utilisation des sessions et l'outil anti-CSRF d'ASP.NET Core.
    public UserSessionController(UserSessionService sessionService, IAntiforgery antiforgery)
    {
        this.sessionService = sessionService;
        this.antiforgery = antiforgery;
    }

    // GET api/account/csrf-token : donne le jeton à joindre aux requêtes qui modifient des données.
    // Il est lié à l'utilisateur : il se demande donc une fois connecté. Il répond 401 sans session.
    [HttpGet("csrf-token")]
    public IActionResult GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);

        // ASP.NET Core remplit toujours ce jeton : s'il manquait, la réponse serait une erreur, jamais un jeton vide.
        if (tokens.RequestToken == null)
        {
            throw new InvalidOperationException("Le jeton anti-CSRF n'a pas été créé.");
        }

        return Ok(new CsrfTokenResponse(tokens.RequestToken));
    }

    // POST api/account/logout : détruit la session côté serveur, puis le cookie (SS §3).
    // Le cookie d'appareil reste : le navigateur reste reconnu à la prochaine connexion.
    // Cette route ne lit aucun corps : la limite empêche seulement qu'on lui en envoie un énorme.
    [HttpPost("logout")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> LogOut()
    {
        var sessionToken = Request.Cookies[SessionCookiesConfiguration.SessionCookieName];

        // La route demande une session : le cookie est donc là. Sans lui, il n'y aurait aucune session à détruire.
        if (sessionToken != null)
        {
            await sessionService.CloseAsync(sessionToken);
        }

        Response.Cookies.Delete(SessionCookiesConfiguration.SessionCookieName, SessionCookiesConfiguration.CreateSessionCookieOptions());

        return Ok(new MessageResponse(LoggedOutMessage));
    }
}
