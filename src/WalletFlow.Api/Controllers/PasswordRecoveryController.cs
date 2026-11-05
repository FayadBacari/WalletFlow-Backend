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
    public const string MessageSentForEveryPasswordRecoveryRequest = "Si un compte existe avec cette adresse, un e-mail vient d'être envoyé.";

    public const string MessageSentWhenThePasswordIsChanged = "Ton mot de passe est changé. Tu peux maintenant te connecter.";

    private readonly PasswordRecoveryRequestService serviceThatRequestsThePasswordRecoveries;
    private readonly PasswordResetService serviceThatChangesTheForgottenPasswords;

    // Reçoit les cas d'utilisation de la récupération.
    public PasswordRecoveryController(PasswordRecoveryRequestService serviceThatRequestsThePasswordRecoveries, PasswordResetService serviceThatChangesTheForgottenPasswords)
    {
        this.serviceThatRequestsThePasswordRecoveries = serviceThatRequestsThePasswordRecoveries;
        this.serviceThatChangesTheForgottenPasswords = serviceThatChangesTheForgottenPasswords;
    }

    // POST api/account/forgot-password : demande un lien de récupération. L'e-mail part en arrière-plan.
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitingConfiguration.PolicyNameForPasswordRecoveryRequestsPerIpAddress)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumNumberOfBytesInOneAccountRequest)]
    public IActionResult RequestPasswordRecoveryFromTheVisitorRequest(ForgotPasswordRequest requestSentByTheVisitorToRecover)
    {
        var resultOfThePasswordRecoveryRequest = serviceThatRequestsThePasswordRecoveries.RequestPasswordRecoveryForThisEmailAddress(
            requestSentByTheVisitorToRecover.Email,
            HttpContext.Connection.RemoteIpAddress);

        if (resultOfThePasswordRecoveryRequest.IsAccepted)
        {
            return Ok(new MessageResponse(MessageSentForEveryPasswordRecoveryRequest));
        }

        if (resultOfThePasswordRecoveryRequest.TooManyRequestsForThisEmailAddress)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        ModelState.AddModelError(nameof(ForgotPasswordRequest.Email), resultOfThePasswordRecoveryRequest.EmailAddressErrorMessage!);
        return ValidationProblem();
    }

    // POST api/account/reset-password : change le mot de passe, avec le jeton du lien.
    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitingConfiguration.PolicyNameForPasswordResetAttemptsPerIpAddress)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumNumberOfBytesInOneAccountRequest)]
    public async Task<IActionResult> ResetPasswordFromTheVisitorRequest(ResetPasswordRequest requestSentByTheVisitorToReset)
    {
        var resultOfThePasswordResetRequest = await serviceThatChangesTheForgottenPasswords.ResetPasswordWithLinkTokenAsync(
            requestSentByTheVisitorToReset.Token,
            requestSentByTheVisitorToReset.NewPassword,
            HttpContext.Connection.RemoteIpAddress);

        if (resultOfThePasswordResetRequest.IsPasswordChanged)
        {
            return Ok(new MessageResponse(MessageSentWhenThePasswordIsChanged));
        }

        if (resultOfThePasswordResetRequest.LinkErrorMessage != null)
        {
            ModelState.AddModelError(nameof(ResetPasswordRequest.Token), resultOfThePasswordResetRequest.LinkErrorMessage);
        }

        if (resultOfThePasswordResetRequest.PasswordErrorMessage != null)
        {
            ModelState.AddModelError(nameof(ResetPasswordRequest.NewPassword), resultOfThePasswordResetRequest.PasswordErrorMessage);
        }

        return ValidationProblem();
    }
}
