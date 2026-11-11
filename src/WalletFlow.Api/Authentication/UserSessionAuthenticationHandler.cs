using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using WalletFlow.Api.Configuration;
using WalletFlow.Application.UserSessions;

namespace WalletFlow.Api.Authentication;

// Reconnaît l'utilisateur à chaque requête, grâce à son cookie de session (SS §3 et §4) :
// l'identifiant de l'utilisateur vient toujours de la session, jamais de la requête.
public class UserSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SessionWalletFlow";
    public const string LoginRequiredMessage = "Connecte-toi pour continuer.";

    private readonly UserSessionService sessionService;
    private readonly IProblemDetailsService problemDetailsService;

    // Reçoit les réglages imposés par le framework, le cas d'utilisation des sessions et le format des erreurs.
    public UserSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        UserSessionService sessionService,
        IProblemDetailsService problemDetailsService)
        : base(options, loggerFactory, encoder)
    {
        this.sessionService = sessionService;
        this.problemDetailsService = problemDetailsService;
    }

    // Lit le cookie de la requête et cherche sa session (nom imposé par le framework).
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sessionToken = Request.Cookies[SessionCookiesConfiguration.SessionCookieName];
        if (sessionToken == null)
        {
            return AuthenticateResult.NoResult();
        }

        var ownerId = await sessionService.FindOwnerAndRecordActivityAsync(sessionToken);
        if (ownerId == null)
        {
            return AuthenticateResult.NoResult();
        }

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, ownerId.Value.ToString()) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    // Répond 401 quand une route demande une session, avec le même format que les autres erreurs (nom imposé par le framework).
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;

        var problemContext = new ProblemDetailsContext { HttpContext = Context };
        problemContext.ProblemDetails.Status = StatusCodes.Status401Unauthorized;
        problemContext.ProblemDetails.Title = LoginRequiredMessage;

        await problemDetailsService.TryWriteAsync(problemContext);
    }
}
