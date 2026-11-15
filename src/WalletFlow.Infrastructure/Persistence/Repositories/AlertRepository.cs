using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Notifications;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les types d'alerte, les choix des utilisateurs et les alertes envoyées dans la base PostgreSQL.
// Les objets lus ici sont suivis par EF Core, sauf mention contraire : l'unité de travail enregistre ce que leurs règles
// ont changé (voir IDatabaseUnitOfWork).
public class AlertRepository : IAlertRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public AlertRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Donne les 8 types d'alerte, que le script SQL a remplis.
    public async Task<List<AlertType>> FindTypesAsync()
    {
        return await databaseContext.AlertTypes.AsNoTracking().ToListAsync();
    }

    // Donne les choix enregistrés de l'utilisateur, toujours lus en base.
    public async Task<List<AlertPreference>> FindPreferencesAsync(Guid userId)
    {
        return await databaseContext.AlertPreferences
            .AsNoTracking()
            .Where(preference => preference.UserId == userId)
            .ToListAsync();
    }

    // Cherche le choix de l'utilisateur pour ce type d'alerte, par la clé primaire de parametrer.
    public async Task<AlertPreference?> FindPreferenceAsync(Guid userId, AlertKind kind)
    {
        return await databaseContext.AlertPreferences.SingleOrDefaultAsync(preference =>
            preference.UserId == userId
            && preference.Kind == kind);
    }

    // Un type est activé tant qu'aucun choix enregistré ne le désactive.
    public async Task<bool> IsEnabledAsync(Guid userId, AlertKind kind)
    {
        return !await databaseContext.AlertPreferences.AnyAsync(preference =>
            preference.UserId == userId
            && preference.Kind == kind
            && !preference.IsEnabled);
    }

    // Donne les alertes de budget de l'utilisateur pour ce mois, par l'index ix_alerte_utilisateur.
    public async Task<List<Alert>> FindBudgetAlertsAsync(Guid userId, DateOnly month)
    {
        return await databaseContext.Alerts
            .AsNoTracking()
            .Where(alert =>
                alert.UserId == userId
                && alert.Kind == AlertKind.BudgetReached
                && alert.Month == month)
            .ToListAsync();
    }

    // Cherche l'alerte d'objectif du sac, par l'index ix_alerte_sac.
    public async Task<Alert?> FindBagAlertAsync(Guid bagId)
    {
        return await databaseContext.Alerts.SingleOrDefaultAsync(alert =>
            alert.BagId == bagId
            && alert.Kind == AlertKind.BagGoalReached);
    }

    // Cherche l'alerte d'autorisation de la connexion, par l'index ix_alerte_connexion.
    public async Task<Alert?> FindConnectionAlertAsync(Guid connectionId)
    {
        return await databaseContext.Alerts.SingleOrDefaultAsync(alert =>
            alert.ConnectionId == connectionId
            && alert.Kind == AlertKind.BankAuthorizationToRenew);
    }

    // Cherche en SQL les connexions à prévenir : une autorisation qui finit avant la limite, sans alerte, et sans choix
    // de l'utilisateur qui coupe cette alerte.
    public async Task<List<Guid>> FindConnectionsToWarnAsync(DateTime authorizationEndLimit)
    {
        return await databaseContext.BankConnections
            .AsNoTracking()
            .Where(connection =>
                connection.AuthorizationExpiresAt <= authorizationEndLimit
                && !databaseContext.Alerts.Any(alert =>
                    alert.ConnectionId == connection.Id
                    && alert.Kind == AlertKind.BankAuthorizationToRenew)
                && !databaseContext.AlertPreferences.Any(preference =>
                    preference.UserId == connection.UserId
                    && preference.Kind == AlertKind.BankAuthorizationToRenew
                    && !preference.IsEnabled))
            .Select(connection => connection.Id)
            .ToListAsync();
    }

    // Lit l'alerte, puis le nom de ce qu'elle vise. Si la catégorie, le sac ou la connexion vient de disparaître,
    // l'alerte est partie avec (ON DELETE CASCADE) : il n'y a plus rien à envoyer.
    public async Task<AlertEmailContent?> ReadEmailContentAsync(Guid alertId)
    {
        var alert = await databaseContext.Alerts.AsNoTracking().SingleOrDefaultAsync(saved => saved.Id == alertId);

        if (alert == null)
        {
            return null;
        }

        if (alert.Kind == AlertKind.BudgetReached
            && alert.CategoryId is Guid categoryId
            && alert.Month is DateOnly budgetMonth
            && alert.Threshold is short threshold)
        {
            var categoryName = await databaseContext.Categories
                .AsNoTracking()
                .Where(category => category.Id == categoryId)
                .Select(category => category.Name)
                .SingleOrDefaultAsync();

            return categoryName == null
                ? null
                : new AlertEmailContent.BudgetReached { CategoryName = categoryName, Month = budgetMonth, Threshold = threshold };
        }

        if (alert.Kind == AlertKind.NewSuggestion && alert.Month is DateOnly suggestionMonth)
        {
            return new AlertEmailContent.NewSuggestions { Month = suggestionMonth };
        }

        if (alert.Kind == AlertKind.BagGoalReached && alert.BagId is Guid bagId)
        {
            var bagName = await databaseContext.Bags
                .AsNoTracking()
                .Where(bag => bag.Id == bagId)
                .Select(bag => bag.Name)
                .SingleOrDefaultAsync();

            return bagName == null ? null : new AlertEmailContent.BagGoalReached { BagName = bagName };
        }

        if (alert.Kind == AlertKind.BankAuthorizationToRenew && alert.ConnectionId is Guid connectionId)
        {
            var connection = await databaseContext.BankConnections
                .AsNoTracking()
                .Where(saved => saved.Id == connectionId)
                .Select(saved => new { saved.BankName, saved.AuthorizationExpiresAt })
                .SingleOrDefaultAsync();

            return connection == null
                ? null
                : new AlertEmailContent.BankAuthorizationToRenew
                {
                    BankName = connection.BankName,
                    AuthorizationExpiresAt = connection.AuthorizationExpiresAt
                };
        }

        throw new InvalidOperationException("Cette alerte n'a pas ce que son type demande : la base l'aurait refusée (ck_alerte_contenu).");
    }

    // Garde de côté le nouveau choix : il ne sera écrit qu'avec les autres changements.
    public void PreparePreference(AlertPreference preference)
    {
        databaseContext.AlertPreferences.Add(preference);
    }

    // Garde de côté la nouvelle alerte : elle ne sera écrite qu'avec les autres changements.
    public void PrepareAlert(Alert alert)
    {
        databaseContext.Alerts.Add(alert);
    }

    // Prépare l'effacement de l'alerte. La base efface avec elle son e-mail s'il attend encore (ON DELETE CASCADE).
    public void PrepareDeletion(Alert alert)
    {
        databaseContext.Alerts.Remove(alert);
    }

    // Une seule instruction. Seules les alertes de budget et de suggestion ont un mois : la base le garantit
    // (ck_alerte_contenu), et une alerte de sac ou de connexion n'est jamais touchée. Cette écriture est immédiate :
    // elle refuse de partir si des changements attendent l'unité de travail.
    public async Task<int> DeleteMonthAlertsBeforeAsync(DateOnly firstKeptMonth)
    {
        databaseContext.ThrowIfChangesArePending();

        return await databaseContext.Alerts
            .Where(alert => alert.Month < firstKeptMonth)
            .ExecuteDeleteAsync();
    }
}
