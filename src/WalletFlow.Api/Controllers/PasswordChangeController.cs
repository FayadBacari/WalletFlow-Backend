using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.AccountSettings;

namespace WalletFlow.Api.Controllers;

// Changement du mot de passe par l'utilisateur connecté (GR Compte, SS §2 et §3).
// Cette route n'a aucun attribut de sécurité : toute route demande par défaut une session et le jeton anti-CSRF (voir Program.cs).
[ApiController]
[Route("api/account")]
public class PasswordChangeController : ControllerBase
{
    public const string PasswordChangedMessage = "Ton mot de passe est changé. Tes autres appareils sont déconnectés.";

    private readonly PasswordChangeService passwordChangeService;

    // Reçoit le cas d'utilisation du changement de mot de passe.
    public PasswordChangeController(PasswordChangeService passwordChangeService)
    {
        this.passwordChangeService = passwordChangeService;
    }

    // POST api/account/password : le mot de passe actuel, et le code si la double authentification est activée, changent le mot de passe.
    [HttpPost("password")]
    [EnableRateLimiting(RateLimitingConfiguration.PasswordChangePolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var result = await passwordChangeService.ChangeAsync(new PasswordChangeAttempt
        {
            UserId = SessionClaims.ReadUserId(User),
            CurrentPassword = request.CurrentPassword,
            NewPassword = request.NewPassword,
            Code = request.Code,
            IpAddress = HttpContext.Connection.RemoteIpAddress
        });

        if (result is PasswordChangeResult.Changed changed)
        {
            // La session en cours continue avec son nouvel identifiant : une copie volée de l'ancien cookie ne sert plus (SS §3).
            Response.Cookies.Append(SessionCookiesConfiguration.SessionCookieName, changed.SessionToken, SessionCookiesConfiguration.CreateSessionCookieOptions());
            return Ok(new MessageResponse(PasswordChangedMessage));
        }

        if (result is PasswordChangeResult.NewPasswordBreaksRules)
        {
            ModelState.AddModelError(nameof(ChangePasswordRequest.NewPassword), ErrorMessages.PasswordBreaksRules);
            return ValidationProblem();
        }

        if (result is PasswordChangeResult.WrongPassword)
        {
            ModelState.AddModelError(nameof(ChangePasswordRequest.CurrentPassword), ErrorMessages.WrongCurrentPassword);
            return ValidationProblem();
        }

        // Le même message pour les deux champs : l'erreur ne dit pas lequel est faux.
        ModelState.AddModelError(ApiErrorConfiguration.GeneralErrorsKey, ErrorMessages.WrongPasswordOrCode);
        return ValidationProblem();
    }
}
