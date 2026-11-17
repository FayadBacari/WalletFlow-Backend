using System.Data;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.DataExport;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit dans la base PostgreSQL toutes les données d'un compte pour le fichier de ses données (GR Compte, SS §6).
// Chaque lecture ne prend que les lignes du compte : directement par son identifiant, ou par la banque, la catégorie, le sac ou
// la demande qui le relie au compte. Aucune colonne secrète n'est lue.
public class DataExportRepository : IDataExportRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public DataExportRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Toutes les lectures se font dans une transaction REPEATABLE READ en lecture seule : PostgreSQL leur montre la base telle
    // qu'elle était à la première, et une opération, un sac ou un déplacement enregistré entre deux lectures n'apparaît dans
    // aucune. Une transaction déjà ouverte par l'appelant est gardée : EF Core refuse d'en ouvrir une seconde.
    public async Task<AccountData?> ReadAsync(Guid userId)
    {
        if (databaseContext.Database.CurrentTransaction != null)
        {
            return await ReadAllAsync(userId);
        }

        await using var transaction = await databaseContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await databaseContext.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var data = await ReadAllAsync(userId);
        await transaction.CommitAsync();

        return data;
    }

    // Lit chaque liste du compte, dans un ordre fixe.
    private async Task<AccountData?> ReadAllAsync(Guid userId)
    {
        var profile = await databaseContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new ExportedProfile(user.Email, user.LastName, user.FirstName, user.IsTwoFactorEnabled, user.NotificationChannel))
            .SingleOrDefaultAsync();

        if (profile == null)
        {
            return null;
        }

        var connections = databaseContext.BankConnections.AsNoTracking().Where(connection => connection.UserId == userId);
        var accounts = databaseContext.BankAccounts.AsNoTracking()
            .Where(account => connections.Any(connection => connection.Id == account.BankConnectionId));
        var categories = databaseContext.Categories.AsNoTracking().Where(category => category.UserId == userId);
        var bags = databaseContext.Bags.AsNoTracking().Where(bag => bag.UserId == userId);
        var requests = databaseContext.SupportRequests.AsNoTracking().Where(request => request.UserId == userId);

        return new AccountData(
            profile,
            await connections
                .OrderBy(connection => connection.BankName).ThenBy(connection => connection.Id)
                .Select(connection => new ExportedBankConnection(connection.Id, connection.BankName, connection.Status, connection.AuthorizationExpiresAt))
                .ToListAsync(),
            await accounts
                .OrderBy(account => account.Name).ThenBy(account => account.Id)
                .Select(account => new ExportedBankAccount(account.Id, account.BankConnectionId, account.Name, account.BalanceInCents, account.BalanceUpdatedAt))
                .ToListAsync(),
            await databaseContext.BankTransactions.AsNoTracking()
                .Where(operation => accounts.Any(account => account.Id == operation.BankAccountId))
                .OrderBy(operation => operation.TransactionDate).ThenBy(operation => operation.Id)
                .Select(operation => new ExportedTransaction(
                    operation.Id,
                    operation.BankAccountId,
                    operation.TransactionDate,
                    operation.Label,
                    operation.AmountInCents,
                    operation.CategoryId,
                    operation.IsInternalTransfer))
                .ToListAsync(),
            await categories
                .OrderBy(category => category.Name).ThenBy(category => category.Id)
                .Select(category => new ExportedCategory(category.Id, category.Name, category.MonthlyBudgetInCents, category.IsOtherCategory))
                .ToListAsync(),
            await databaseContext.CategorizationRules.AsNoTracking()
                .Where(rule => categories.Any(category => category.Id == rule.CategoryId))
                .OrderBy(rule => rule.Label).ThenBy(rule => rule.Id)
                .Select(rule => new ExportedCategorizationRule(rule.Label, rule.CategoryId))
                .ToListAsync(),
            await bags
                .OrderBy(bag => bag.Name).ThenBy(bag => bag.Id)
                .Select(bag => new ExportedBag(bag.Id, bag.Name, bag.TargetAmountInCents, bag.TargetDate))
                .ToListAsync(),
            await databaseContext.BagMovements.AsNoTracking()
                .Where(movement =>
                    bags.Any(bag => bag.Id == movement.SourceBagId)
                    || bags.Any(bag => bag.Id == movement.DestinationBagId))
                .OrderBy(movement => movement.MovedAt).ThenBy(movement => movement.Id)
                .Select(movement => new ExportedBagMovement(
                    movement.Id,
                    movement.MovedAt,
                    movement.AmountInCents,
                    movement.SourceBagId,
                    movement.DestinationBagId,
                    movement.SavingsSuggestionId))
                .ToListAsync(),
            await databaseContext.SavingsSuggestions.AsNoTracking()
                .Where(suggestion => suggestion.UserId == userId)
                .OrderBy(suggestion => suggestion.Month).ThenBy(suggestion => suggestion.Id)
                .Select(suggestion => new ExportedSuggestion(
                    suggestion.Id,
                    suggestion.Month,
                    suggestion.Rule,
                    suggestion.CategoryId,
                    suggestion.MonthlySavingsInCents,
                    suggestion.Status,
                    suggestion.StatusChangedAt))
                .ToListAsync(),
            await ReadAlertSettingsAsync(userId),
            await databaseContext.Alerts.AsNoTracking()
                .Where(alert => alert.UserId == userId)
                .OrderBy(alert => alert.CreatedAt).ThenBy(alert => alert.Id)
                .Select(alert => new ExportedAlert(
                    alert.Id,
                    alert.Kind,
                    alert.CreatedAt,
                    alert.Month,
                    alert.Threshold,
                    alert.CategoryId,
                    alert.BagId,
                    alert.ConnectionId))
                .ToListAsync(),
            await ReadSupportRequestsAsync(requests));
    }

    // Chaque type d'alerte avec le choix de l'utilisateur : activé tant qu'il ne l'a pas désactivé, comme dans ses réglages.
    private async Task<List<ExportedAlertSetting>> ReadAlertSettingsAsync(Guid userId)
    {
        var kinds = await databaseContext.AlertTypes.AsNoTracking().Select(type => type.Kind).ToListAsync();
        var preferences = await databaseContext.AlertPreferences.AsNoTracking()
            .Where(preference => preference.UserId == userId)
            .ToListAsync();

        return kinds
            .Order()
            .Select(kind => new ExportedAlertSetting(
                kind,
                preferences.SingleOrDefault(preference => preference.Kind == kind)?.IsEnabled ?? true))
            .ToList();
    }

    // Les demandes de support du compte, chacune avec ses réponses de la plus ancienne à la plus récente.
    private async Task<List<ExportedSupportRequest>> ReadSupportRequestsAsync(IQueryable<SupportRequest> requests)
    {
        var responses = await databaseContext.SupportResponses.AsNoTracking()
            .Where(response => requests.Any(request => request.Id == response.SupportRequestId))
            .OrderBy(response => response.SentAt).ThenBy(response => response.Id)
            .Select(response => new { response.SupportRequestId, Response = new ExportedSupportResponse(response.SentAt, response.Content) })
            .ToListAsync();

        var savedRequests = await requests
            .OrderBy(request => request.SentAt).ThenBy(request => request.Id)
            .Select(request => new { request.Id, request.SentAt, request.Status, request.Message })
            .ToListAsync();

        return savedRequests
            .Select(request => new ExportedSupportRequest(
                request.Id,
                request.SentAt,
                request.Status,
                request.Message,
                responses.Where(response => response.SupportRequestId == request.Id).Select(response => response.Response).ToList()))
            .ToList();
    }
}
