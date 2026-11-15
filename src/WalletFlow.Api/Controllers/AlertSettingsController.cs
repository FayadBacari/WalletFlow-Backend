using Microsoft.AspNetCore.Mvc;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.Notifications;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Api.Controllers;

// Réglages d'alerte de l'utilisateur connecté (GR Notif) : le canal qui reçoit ses alertes, et chaque type d'alerte.
// Ces routes n'ont aucun attribut de sécurité : toute route demande par défaut une session et le jeton anti-CSRF
// (voir Program.cs). Elles ne prennent aucun identifiant : chacune ne touche que le compte de la session (SS §4).
[ApiController]
[Route("api/alert-settings")]
public class AlertSettingsController : ControllerBase
{
    // Le nom de chaque type d'alerte tel que le site le reçoit et l'envoie.
    private static readonly Dictionary<AlertKind, string> TypeNames = new Dictionary<AlertKind, string>
    {
        { AlertKind.BudgetReached, "budgetReached" },
        { AlertKind.NewSuggestion, "newSuggestion" },
        { AlertKind.BagGoalReached, "bagGoalReached" },
        { AlertKind.BankAuthorizationToRenew, "bankAuthorizationToRenew" },
        { AlertKind.NewDevice, "newDevice" },
        { AlertKind.AccountLocked, "accountLocked" },
        { AlertKind.PasswordChange, "passwordChange" },
        { AlertKind.EmailChange, "emailChange" },
    };

    // Le nom de chaque canal tel que le site le reçoit et l'envoie.
    private static readonly Dictionary<NotificationChannel, string> ChannelNames = new Dictionary<NotificationChannel, string>
    {
        { NotificationChannel.Email, "email" },
        { NotificationChannel.Phone, "phone" },
        { NotificationChannel.EmailAndPhone, "emailAndPhone" },
    };

    private readonly AlertSettingsService settingsService;

    // Reçoit le cas d'utilisation des réglages d'alerte.
    public AlertSettingsController(AlertSettingsService settingsService)
    {
        this.settingsService = settingsService;
    }

    // GET api/alert-settings : le canal et les 8 types d'alerte, avec le choix de l'utilisateur. Ce sont des données
    // personnelles : aucun navigateur ni intermédiaire ne doit les garder.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Read()
    {
        var settings = await settingsService.ReadAsync(SessionClaims.ReadUserId(User));

        if (settings == null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: UserSessionAuthenticationHandler.LoginRequiredMessage);
        }

        return Ok(CreateResponse(settings));
    }

    // PUT api/alert-settings/channel : garde le canal choisi. Les alertes partent par e-mail tant que l'application mobile n'existe pas.
    [HttpPut("channel")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAlertSettingsRequestBytes)]
    public async Task<IActionResult> ChangeChannel(AlertChannelRequest request)
    {
        if (!TryReadName(ChannelNames, request.Channel, out var channel))
        {
            ModelState.AddModelError(nameof(AlertChannelRequest.Channel), ErrorMessages.AlertChannelUnknown);
            return ValidationProblem();
        }

        return Answer(await settingsService.ChangeChannelAsync(SessionClaims.ReadUserId(User), channel));
    }

    // PUT api/alert-settings/alerts/{type} : active ou désactive ce type d'alerte. Un type inconnu répond comme une adresse
    // qui n'existe pas ; une alerte de sécurité ne se désactive pas.
    [HttpPut("alerts/{type}")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAlertSettingsRequestBytes)]
    public async Task<IActionResult> Switch(string type, AlertSwitchRequest request)
    {
        if (!TryReadName(TypeNames, type, out var kind))
        {
            return Problem(statusCode: StatusCodes.Status404NotFound);
        }

        return Answer(await settingsService.SwitchAsync(SessionClaims.ReadUserId(User), kind, request.IsEnabled));
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

    // Retrouve la valeur qui porte exactement ce nom, majuscules comprises. Renvoie faux pour tout autre nom.
    private static bool TryReadName<TValue>(Dictionary<TValue, string> names, string? name, out TValue value)
        where TValue : struct, Enum
    {
        foreach (var pair in names)
        {
            if (string.Equals(pair.Value, name, StringComparison.Ordinal))
            {
                value = pair.Key;
                return true;
            }
        }

        value = default;
        return false;
    }

    // Donne les réglages tels que le site les reçoit.
    private static AlertSettingsResponse CreateResponse(AlertSettings settings)
    {
        var alerts = settings.Alerts
            .Select(setting => new AlertSettingResponse(TypeNames[setting.Kind], setting.Label, setting.CanBeDisabled, setting.IsEnabled))
            .ToList();

        return new AlertSettingsResponse(ChannelNames[settings.Channel], alerts);
    }
}
