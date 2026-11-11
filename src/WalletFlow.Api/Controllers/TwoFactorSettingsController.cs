using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Api.Controllers;

// Réglage de la double authentification par l'utilisateur connecté (GR Auth, SS §2).
// Ces routes n'ont aucun attribut de sécurité : toute route demande par défaut une session et le jeton anti-CSRF (voir Program.cs).
// Leurs réponses contiennent un secret ou des codes de secours : aucun navigateur ni intermédiaire ne doit les garder.
[ApiController]
[Route("api/account/two-factor")]
[EnableRateLimiting(RateLimitingConfiguration.TwoFactorSettingsPolicy)]
[RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
    public async Task<IActionResult> StartActivation(StartTwoFactorActivationRequest request)
    {
        var result = await activationService.StartAsync(
            ReadUserId(),
            request.Password,
            HttpContext.Connection.RemoteIpAddress);

        if (result is TwoFactorActivationStartResult.Started started)
        {
            return Ok(new TwoFactorSecretResponse(started.Secret, started.SetupLink));
        }

        if (result is TwoFactorActivationStartResult.AlreadyEnabled)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.TwoFactorAlreadyEnabled);
        }

        ModelState.AddModelError(nameof(StartTwoFactorActivationRequest.Password), ErrorMessages.WrongActivationPassword);
        return ValidationProblem();
    }

    // POST api/account/two-factor/finish-activation : un premier code juste active tout, et donne les 10 codes de secours.
    [HttpPost("finish-activation")]
    public async Task<IActionResult> FinishActivation(TwoFactorCodeRequest request)
    {
        var result = await activationService.FinishAsync(
            ReadUserId(),
            request.Code,
            HttpContext.Connection.RemoteIpAddress);

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
    public async Task<IActionResult> Deactivate(DeactivateTwoFactorRequest request)
    {
        var result = await deactivationService.DeactivateAsync(
            ReadUserId(),
            request.Password,
            request.Code,
            HttpContext.Connection.RemoteIpAddress);

        if (result == TwoFactorDeactivationResult.TwoFactorDeactivated)
        {
            return Ok(new MessageResponse(DeactivatedMessage));
        }

        if (result == TwoFactorDeactivationResult.TwoFactorNotEnabled)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.TwoFactorNotEnabled);
        }

        // Le même message pour les deux champs : l'erreur ne dit pas lequel est faux.
        ModelState.AddModelError(ApiErrorConfiguration.GeneralErrorsKey, ErrorMessages.WrongPasswordOrCode);
        return ValidationProblem();
    }

    // L'identifiant de l'utilisateur vient toujours de la session, jamais de la requête (SS §4).
    private Guid ReadUserId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Le gestionnaire des sessions écrit toujours cet identifiant : sans lui, la requête n'aurait pas passé l'authentification.
        if (claimValue == null)
        {
            throw new InvalidOperationException("La session ne donne pas l'identifiant de son propriétaire.");
        }

        return Guid.Parse(claimValue);
    }
}
