using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.DataExport;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Api.Controllers;

// Téléchargement de toutes ses données par l'utilisateur connecté (GR Compte, droit à la portabilité du RGPD ; SS §2 et §6).
// Cette route demande une session, que [Authorize] redit, et le jeton anti-CSRF, comme toute route (voir Program.cs).
[ApiController]
[Route("api/account")]
[Authorize]
public class DataExportController : ControllerBase
{
    public const string FileNamePrefix = "walletflow-donnees-";

    private readonly DataExportService exportService;

    // Reçoit le cas d'utilisation du téléchargement.
    public DataExportController(DataExportService exportService)
    {
        this.exportService = exportService;
    }

    // POST api/account/export : le mot de passe, et le code si la double authentification est activée, donnent le fichier de
    // toutes les données du compte, à enregistrer sous « walletflow-donnees-AAAA-MM-JJ.json ». Le fichier se télécharge
    // directement, il n'est jamais envoyé par e-mail, et rien ne doit le garder en cache (SS §6). POST et non GET : la requête
    // porte le mot de passe, et chaque téléchargement est noté au journal.
    [HttpPost("export")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    [ProducesResponseType<DataExportFile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = OpenApiConfiguration.ServerBusyDescription)]
    public async Task<IActionResult> Export(DataExportRequest request, CancellationToken cancellationToken = default)
    {
        var result = await exportService.ExportAsync(new DataExportAttempt
        {
            UserId = SessionClaims.ReadUserId(User),
            Password = request.Password,
            Code = request.Code,
            IpAddress = HttpContext.Connection.RemoteIpAddress
        },
        cancellationToken);

        if (result is DataExportResult.Exported exported)
        {
            var day = UsersCalendar.FindToday(new DateTimeOffset(exported.ExportedAt, TimeSpan.Zero));
            var disposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = FileNamePrefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json"
            };
            Response.Headers.ContentDisposition = disposition.ToString();
            return Ok(CreateFile(exported));
        }

        if (result is DataExportResult.TooManyExports)
        {
            return this.AnswerTooManyRequests(RetryDelays.DataExport);
        }

        if (result is DataExportResult.AccountGone)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: UserSessionAuthenticationHandler.LoginRequiredMessage);
        }

        if (result is DataExportResult.WrongPassword)
        {
            ModelState.AddModelError(nameof(DataExportRequest.Password), ErrorMessages.WrongCurrentPassword);
            return ValidationProblem();
        }

        // Le même message pour les deux champs : l'erreur ne dit pas lequel est faux.
        ModelState.AddModelError(ApiErrorConfiguration.GeneralErrorsKey, ErrorMessages.WrongPasswordOrCode);
        return ValidationProblem();
    }

    // Donne le fichier tel que l'utilisateur le télécharge.
    private static DataExportFile CreateFile(DataExportResult.Exported exported)
    {
        var data = exported.Data;

        return new DataExportFile(
            exported.ExportedAt,
            new ProfileInFile(
                data.Profile.Email,
                data.Profile.LastName,
                data.Profile.FirstName,
                data.Profile.IsTwoFactorEnabled,
                WebsiteNames.Of(data.Profile.NotificationChannel)),
            data.BankConnections
                .Select(connection => new BankConnectionInFile(
                    connection.Id,
                    connection.BankName,
                    WebsiteNames.Of(connection.Status),
                    connection.AuthorizationExpiresAt))
                .ToList(),
            data.BankAccounts
                .Select(account => new BankAccountInFile(account.Id, account.BankConnectionId, account.Name, account.BalanceInCents, account.BalanceUpdatedAt))
                .ToList(),
            data.Transactions
                .Select(operation => new TransactionInFile(
                    operation.Id,
                    operation.BankAccountId,
                    operation.Date,
                    operation.Label,
                    operation.AmountInCents,
                    operation.CategoryId,
                    operation.IsInternalTransfer))
                .ToList(),
            data.Categories
                .Select(category => new CategoryInFile(category.Id, category.Name, category.MonthlyBudgetInCents, category.IsOtherCategory))
                .ToList(),
            data.CategorizationRules
                .Select(rule => new CategorizationRuleInFile(rule.Label, rule.CategoryId))
                .ToList(),
            data.Bags
                .Select(bag => new BagInFile(bag.Id, bag.Name, bag.TargetAmountInCents, bag.TargetDate))
                .ToList(),
            data.BagMovements
                .Select(movement => new BagMovementInFile(
                    movement.Id,
                    movement.MovedAt,
                    movement.AmountInCents,
                    movement.SourceBagId,
                    movement.DestinationBagId,
                    movement.SuggestionId))
                .ToList(),
            data.Suggestions
                .Select(suggestion => new SuggestionInFile(
                    suggestion.Id,
                    suggestion.Month,
                    WebsiteNames.Of(suggestion.Rule),
                    suggestion.CategoryId,
                    suggestion.MonthlySavingsInCents,
                    WebsiteNames.Of(suggestion.Status),
                    suggestion.StatusChangedAt))
                .ToList(),
            data.AlertSettings
                .Select(setting => new AlertSettingInFile(WebsiteNames.Of(setting.Kind), setting.IsEnabled))
                .ToList(),
            data.Alerts
                .Select(alert => new AlertInFile(
                    alert.Id,
                    WebsiteNames.Of(alert.Kind),
                    alert.CreatedAt,
                    alert.Month,
                    alert.Threshold,
                    alert.CategoryId,
                    alert.BagId,
                    alert.BankConnectionId))
                .ToList(),
            data.SupportRequests
                .Select(request => new SupportRequestInFile(
                    request.Id,
                    request.SentAt,
                    WebsiteNames.Of(request.Status),
                    request.Message,
                    request.Responses.Select(response => new SupportResponseInFile(response.SentAt, response.Content)).ToList()))
                .ToList());
    }
}
