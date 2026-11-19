using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Api.Controllers;

// Réglage de la double authentification par l'utilisateur connecté (GR Auth, SS §2).
// Ces routes demandent une session, que [Authorize] redit, et le jeton anti-CSRF, comme toute route (voir Program.cs).
// Leurs réponses contiennent un secret ou des codes de secours : aucun navigateur ni intermédiaire ne doit les garder.
[ApiController]
[Route("api/account/two-factor")]
[Authorize]
[EnableRateLimiting(RateLimitingConfiguration.TwoFactorSettingsPolicy)]
[RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
public class TwoFactorSettingsController : ControllerBase
{
    public const string DeactivatedMessage = "La double authentification est désactivée.";

    private readonly TwoFactorActivationService activationService;
    private readonly TwoFactorDeactivationService deactivationService;

    // Reçoit les cas d'utilisation du réglage de la double authentification.
    public TwoFactorSettingsController(TwoFactorActivationService activationService, TwoFactorDeactivationService deactivationService)
    {
        this.activationService = activationService;
        this.deactivationService = deactivationService;
    }

    // POST api/account/two-factor/start-activation : le mot de passe redemandé donne le secret à ajouter à l'application.
    [HttpPost("start-activation")]
    [ProducesResponseType<TwoFactorSecretResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.ServerBusyDescription)]
    public async Task<IActionResult> StartActivation(StartTwoFactorActivationRequest request, CancellationToken cancellationToken = default)
    {
        var result = await activationService.StartAsync(
            SessionClaims.ReadUserId(User),
            request.Password,
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        if (result is TwoFactorActivationStartResult.Started started)
        {
            return Ok(new TwoFactorSecretResponse(started.Secret, started.SetupLink));
        }

        if (result is TwoFactorActivationStartResult.AlreadyEnabled)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.TwoFactorAlreadyEnabled);
        }

        ModelState.AddModelError(nameof(StartTwoFactorActivationRequest.Password), ErrorMessages.WrongCurrentPassword);
        return ValidationProblem();
    }

    // POST api/account/two-factor/finish-activation : un premier code juste active tout, et donne les 10 codes de secours.
    [HttpPost("finish-activation")]
    [ProducesResponseType<TwoFactorBackupCodesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.ServerBusyDescription)]
    public async Task<IActionResult> FinishActivation(TwoFactorCodeRequest request, CancellationToken cancellationToken = default)
    {
        var result = await activationService.FinishAsync(
            SessionClaims.ReadUserId(User),
            request.Code,
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        if (result is TwoFactorActivationFinishResult.Activated activated)
        {
            return Ok(new TwoFactorBackupCodesResponse(activated.BackupCodes));
        }

        if (result is TwoFactorActivationFinishResult.ActivationMustRestart)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.ActivationMustRestart);
        }

        ModelState.AddModelError(nameof(TwoFactorCodeRequest.Code), ErrorMessages.WrongTwoFactorCode);
        return ValidationProblem();
    }

    // POST api/account/two-factor/deactivate : le mot de passe et un code désactivent tout.
    [HttpPost("deactivate")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.ServerBusyDescription)]
    public async Task<IActionResult> Deactivate(DeactivateTwoFactorRequest request, CancellationToken cancellationToken = default)
    {
        var result = await deactivationService.DeactivateAsync(
            SessionClaims.ReadUserId(User),
            request.Password,
            request.Code,
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        if (result is TwoFactorDeactivationResult.TwoFactorDeactivated)
        {
            return Ok(new MessageResponse(DeactivatedMessage));
        }

        if (result is TwoFactorDeactivationResult.TwoFactorNotEnabled)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.TwoFactorNotEnabled);
        }

        // Le même message pour les deux champs : l'erreur ne dit pas lequel est faux.
        ModelState.AddModelError(ApiErrorConfiguration.GeneralErrorsKey, ErrorMessages.WrongPasswordOrCode);
        return ValidationProblem();
    }
}
