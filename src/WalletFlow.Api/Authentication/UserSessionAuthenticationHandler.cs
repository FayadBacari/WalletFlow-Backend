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
    public const string NameOfTheUserSessionAuthenticationScheme = "SessionWalletFlow";
    public const string MessageShownWhenTheVisitorMustLogIn = "Connecte-toi pour continuer.";

    private readonly UserSessionService serviceThatRecognizesTheUserSessions;
    private readonly IProblemDetailsService serviceThatWritesTheErrorAnswers;

    // Reçoit les réglages imposés par le framework, le cas d'utilisation des sessions et le format des erreurs.
    public UserSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> optionsOfTheUserAuthenticationScheme,
        ILoggerFactory factoryOfTheLoggersOfTheAuthentication,
        UrlEncoder encoderOfTheAddressesInTheAuthentication,
        UserSessionService serviceThatRecognizesTheUserSessions,
        IProblemDetailsService serviceThatWritesTheErrorAnswers)
        : base(optionsOfTheUserAuthenticationScheme, factoryOfTheLoggersOfTheAuthentication, encoderOfTheAddressesInTheAuthentication)
    {
        this.serviceThatRecognizesTheUserSessions = serviceThatRecognizesTheUserSessions;
        this.serviceThatWritesTheErrorAnswers = serviceThatWritesTheErrorAnswers;
    }

    // Lit le cookie de la requête et cherche sa session (nom imposé par le framework).
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sessionTokenReadInTheCookie = Request.Cookies[SessionCookiesConfiguration.NameOfTheUserSessionCookie];
        if (sessionTokenReadInTheCookie == null)
        {
            return AuthenticateResult.NoResult();
        }

        var identifierOfTheOwnerOfTheSession = await serviceThatRecognizesTheUserSessions.FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(sessionTokenReadInTheCookie);
        if (identifierOfTheOwnerOfTheSession == null)
        {
            return AuthenticateResult.NoResult();
        }

        var claimsOfTheLoggedInUser = new[] { new Claim(ClaimTypes.NameIdentifier, identifierOfTheOwnerOfTheSession.Value.ToString()) };
        var identityOfTheLoggedInUser = new ClaimsIdentity(claimsOfTheLoggedInUser, NameOfTheUserSessionAuthenticationScheme);
        var ticketOfTheLoggedInUser = new AuthenticationTicket(new ClaimsPrincipal(identityOfTheLoggedInUser), NameOfTheUserSessionAuthenticationScheme);

        return AuthenticateResult.Success(ticketOfTheLoggedInUser);
    }

    // Répond 401 quand une route demande une session, avec le même format que les autres erreurs (nom imposé par le framework).
    protected override async Task HandleChallengeAsync(AuthenticationProperties propertiesOfTheUserAuthenticationChallenge)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;

        var contextOfTheErrorAnswerBeingWritten = new ProblemDetailsContext { HttpContext = Context };
        contextOfTheErrorAnswerBeingWritten.ProblemDetails.Status = StatusCodes.Status401Unauthorized;
        contextOfTheErrorAnswerBeingWritten.ProblemDetails.Title = MessageShownWhenTheVisitorMustLogIn;

        await serviceThatWritesTheErrorAnswers.TryWriteAsync(contextOfTheErrorAnswerBeingWritten);
    }
}
