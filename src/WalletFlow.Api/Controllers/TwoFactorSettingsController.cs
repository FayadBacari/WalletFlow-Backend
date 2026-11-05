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
[EnableRateLimiting(RateLimitingConfiguration.PolicyNameForTwoFactorSettingsRequestsPerIpAddress)]
[RequestSizeLimit(RequestSizeConfiguration.MaximumNumberOfBytesInOneAccountRequest)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TwoFactorSettingsController : ControllerBase
{
    public const string MessageSentWhenTheTwoFactorIsDeactivated = "La double authentification est désactivée.";

    private readonly TwoFactorActivationService serviceThatActivatesTheTwoFactor;
    private readonly TwoFactorDeactivationService serviceThatDeactivatesTheTwoFactor;

    // Reçoit les cas d'utilisation du réglage de la double authentification.
    public TwoFactorSettingsController(TwoFactorActivationService serviceThatActivatesTheTwoFactor, TwoFactorDeactivationService serviceThatDeactivatesTheTwoFactor)
    {
        this.serviceThatActivatesTheTwoFactor = serviceThatActivatesTheTwoFactor;
        this.serviceThatDeactivatesTheTwoFactor = serviceThatDeactivatesTheTwoFactor;
    }

    // POST api/account/two-factor/start-activation : le mot de passe redemandé donne le secret à ajouter à l'application.
    [HttpPost("start-activation")]
    public async Task<IActionResult> StartActivationFromTheUserRequest(StartTwoFactorActivationRequest requestSentByTheUserToStartActivation)
    {
        var resultOfTheStartOfTheActivation = await serviceThatActivatesTheTwoFactor.StartActivationWithThePasswordOfTheUserAsync(
            ReadIdentifierOfTheLoggedInUserFromTheSession(),
            requestSentByTheUserToStartActivation.Password,
            HttpContext.Connection.RemoteIpAddress);

        if (resultOfTheStartOfTheActivation.IsActivationStarted)
        {
            return Ok(new TwoFactorSecretResponse(resultOfTheStartOfTheActivation.SecretOfTheAuthenticatorApplication!, resultOfTheStartOfTheActivation.LinkThatAddsTheSecretToTheApplication!));
        }

        if (resultOfTheStartOfTheActivation.AlreadyEnabledErrorMessage != null)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: resultOfTheStartOfTheActivation.AlreadyEnabledErrorMessage);
        }

        ModelState.AddModelError(nameof(StartTwoFactorActivationRequest.Password), resultOfTheStartOfTheActivation.WrongPasswordErrorMessage!);
        return ValidationProblem();
    }

    // POST api/account/two-factor/finish-activation : un premier code juste active tout, et donne les 10 codes de secours.
    [HttpPost("finish-activation")]
    public async Task<IActionResult> FinishActivationFromTheUserRequest(TwoFactorCodeRequest requestSentByTheUserWithTheFirstCode)
    {
        var resultOfTheEndOfTheActivation = await serviceThatActivatesTheTwoFactor.FinishActivationWithTheFirstCodeOfTheUserAsync(
            ReadIdentifierOfTheLoggedInUserFromTheSession(),
            requestSentByTheUserWithTheFirstCode.Code,
            HttpContext.Connection.RemoteIpAddress);

        if (resultOfTheEndOfTheActivation.IsTwoFactorActivated)
        {
            return Ok(new TwoFactorBackupCodesResponse(resultOfTheEndOfTheActivation.BackupCodesToGiveOnlyOnce!));
        }

        if (resultOfTheEndOfTheActivation.ActivationMustRestartErrorMessage != null)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: resultOfTheEndOfTheActivation.ActivationMustRestartErrorMessage);
        }

        ModelState.AddModelError(nameof(TwoFactorCodeRequest.Code), resultOfTheEndOfTheActivation.WrongCodeErrorMessage!);
        return ValidationProblem();
    }

    // POST api/account/two-factor/deactivate : le mot de passe et un code désactivent tout.
    [HttpPost("deactivate")]
    public async Task<IActionResult> DeactivateTwoFactorFromTheUserRequest(DeactivateTwoFactorRequest requestSentByTheUserToDeactivate)
    {
        var resultOfTheDeactivationOfTheTwoFactor = await serviceThatDeactivatesTheTwoFactor.DeactivateTwoFactorWithPasswordAndCodeOfTheUserAsync(
            ReadIdentifierOfTheLoggedInUserFromTheSession(),
            requestSentByTheUserToDeactivate.Password,
            requestSentByTheUserToDeactivate.Code,
            HttpContext.Connection.RemoteIpAddress);

        if (resultOfTheDeactivationOfTheTwoFactor.IsTwoFactorDeactivated)
        {
            return Ok(new MessageResponse(MessageSentWhenTheTwoFactorIsDeactivated));
        }

        if (resultOfTheDeactivationOfTheTwoFactor.NotEnabledErrorMessage != null)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: resultOfTheDeactivationOfTheTwoFactor.NotEnabledErrorMessage);
        }

        // Le même message pour les deux champs : l'erreur ne dit pas lequel est faux.
        ModelState.AddModelError(ApiErrorConfiguration.KeyOfTheErrorsOutsideTheRequestFields, resultOfTheDeactivationOfTheTwoFactor.WrongPasswordOrCodeErrorMessage!);
        return ValidationProblem();
    }

    // L'identifiant de l'utilisateur vient toujours de la session, jamais de la requête (SS §4).
    private Guid ReadIdentifierOfTheLoggedInUserFromTheSession()
    {
        return Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
