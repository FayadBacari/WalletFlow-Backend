using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.AccountRecovery;

namespace WalletFlow.Api.Controllers;

// Récupération du mot de passe (GR Auth). Ces routes sont appelées sans session : elles n'ont donc
// ni besoin d'être connecté, ni besoin du jeton anti-CSRF (SS §6).
[ApiController]
[Route("api/account")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class PasswordRecoveryController : ControllerBase
{
    // Le même message pour toute demande acceptée, que l'adresse ait un compte ou non (GR Auth, SS §2).
    public const string RecoveryRequestedMessage = "Si un compte existe avec cette adresse, un e-mail vient d'être envoyé.";

    public const string PasswordChangedMessage = "Ton mot de passe est changé. Tu peux maintenant te connecter.";

    private readonly PasswordRecoveryRequestService recoveryRequestService;
    private readonly PasswordResetService passwordResetService;

    // Reçoit les cas d'utilisation de la récupération.
    public PasswordRecoveryController(PasswordRecoveryRequestService recoveryRequestService, PasswordResetService passwordResetService)
    {
        this.recoveryRequestService = recoveryRequestService;
        this.passwordResetService = passwordResetService;
    }

    // POST api/account/forgot-password : demande un lien de récupération. L'e-mail part en arrière-plan.
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitingConfiguration.PasswordRecoveryPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestRecovery(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var result = await recoveryRequestService.RequestAsync(
            request.Email,
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        if (result is PasswordRecoveryRequestResult.RequestAccepted)
        {
            return Ok(new MessageResponse(RecoveryRequestedMessage));
        }

        if (result is PasswordRecoveryRequestResult.TooManyRequests)
        {
            return this.AnswerTooManyRequests(RetryDelays.PerEmailAddress);
        }

        ModelState.AddModelError(nameof(ForgotPasswordRequest.Email), ErrorMessages.InvalidEmailAddress);
        return ValidationProblem();
    }

    // POST api/account/reset-password : change le mot de passe, avec le jeton du lien.
    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitingConfiguration.PasswordResetPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.ServerBusyDescription)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var result = await passwordResetService.ResetAsync(
            request.Token,
            request.NewPassword,
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        if (result is PasswordResetResult.PasswordChanged)
        {
            return Ok(new MessageResponse(PasswordChangedMessage));
        }

        if (result is PasswordResetResult.PasswordBreaksRules)
        {
            ModelState.AddModelError(nameof(ResetPasswordRequest.NewPassword), ErrorMessages.PasswordBreaksRules);
            return ValidationProblem();
        }

        if (result is PasswordResetResult.PasswordBreached)
        {
            ModelState.AddModelError(nameof(ResetPasswordRequest.NewPassword), ErrorMessages.BreachedPassword);
            return ValidationProblem();
        }

        ModelState.AddModelError(nameof(ResetPasswordRequest.Token), ErrorMessages.RecoveryLinkNoLongerValid);
        return ValidationProblem();
    }
}
