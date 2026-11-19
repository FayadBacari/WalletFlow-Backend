using Microsoft.AspNetCore.Mvc;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Notifications;

namespace WalletFlow.Api.Controllers;

// Réglages d'alerte de l'utilisateur connecté (GR Notif) : le canal qui reçoit ses alertes, et chaque type d'alerte.
// Ces routes n'ont aucun attribut de sécurité : toute route demande par défaut une session et le jeton anti-CSRF
// (voir Program.cs). Elles ne prennent aucun identifiant : chacune ne touche que le compte de la session (SS §4).
[ApiController]
[Route("api/alert-settings")]
public class AlertSettingsController : ControllerBase
{
    private readonly AlertSettingsService settingsService;

    // Reçoit le cas d'utilisation des réglages d'alerte.
    public AlertSettingsController(AlertSettingsService settingsService)
    {
        this.settingsService = settingsService;
    }

    // GET api/alert-settings : le canal et les 10 types d'alerte, avec le choix de l'utilisateur. Ce sont des données
    // personnelles : aucun navigateur ni intermédiaire ne doit les garder.
    [HttpGet]
    [ProducesResponseType<AlertSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Read(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.ReadAsync(SessionClaims.ReadUserId(User), cancellationToken);

        if (settings == null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: UserSessionAuthenticationHandler.LoginRequiredMessage);
        }

        return Ok(CreateResponse(settings));
    }

    // PUT api/alert-settings/channel : garde le canal choisi. Les alertes partent par e-mail tant que l'application mobile n'existe pas.
    [HttpPut("channel")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAlertSettingsRequestBytes)]
    [ProducesResponseType<AlertSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeChannel(AlertChannelRequest request, CancellationToken cancellationToken = default)
    {
        if (!WebsiteNames.TryReadNotificationChannel(request.Channel, out var channel))
        {
            ModelState.AddModelError(nameof(AlertChannelRequest.Channel), ErrorMessages.AlertChannelUnknown);
            return ValidationProblem();
        }

        return Answer(await settingsService.ChangeChannelAsync(SessionClaims.ReadUserId(User), channel, cancellationToken));
    }

    // PUT api/alert-settings/alerts/{type} : active ou désactive ce type d'alerte. Un type inconnu répond comme une adresse
    // qui n'existe pas ; une alerte de sécurité ne se désactive pas.
    [HttpPut("alerts/{type}")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAlertSettingsRequestBytes)]
    [ProducesResponseType<AlertSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Switch(string type, AlertSwitchRequest request, CancellationToken cancellationToken = default)
    {
        if (!WebsiteNames.TryReadAlertKind(type, out var kind))
        {
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        return Answer(await settingsService.SwitchAsync(SessionClaims.ReadUserId(User), kind, request.IsEnabled, cancellationToken));
    }

    // Donne la réponse d'un changement de réglage.
    private IActionResult Answer(AlertSettingsResult result)
    {
        if (result is AlertSettingsResult.Saved saved)
        {
            return Ok(CreateResponse(saved.Settings));
        }

        if (result is AlertSettingsResult.CannotBeDisabled)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.SecurityAlertCannotBeDisabled);
        }

        if (result is AlertSettingsResult.AccountGone)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: UserSessionAuthenticationHandler.LoginRequiredMessage);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.AccountChangedMeanwhile);
    }

    // Donne les réglages tels que le site les reçoit.
    private static AlertSettingsResponse CreateResponse(AlertSettings settings)
    {
        var alerts = settings.Alerts
            .Select(setting => new AlertSettingResponse(WebsiteNames.Of(setting.Kind), setting.Label, setting.CanBeDisabled, setting.IsEnabled))
            .ToList();

        return new AlertSettingsResponse(WebsiteNames.Of(settings.Channel), alerts);
    }
}
