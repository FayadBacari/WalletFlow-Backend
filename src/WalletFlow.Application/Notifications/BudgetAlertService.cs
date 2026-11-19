using WalletFlow.Application.Categories;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Notifications;

// Cas d'utilisation : après tout ce qui change les dépenses du mois ou un budget, vérifie les seuils de budget de l'utilisateur
// (GR Dépenses) : 80 %, puis 100 %, une alerte par catégorie, par mois et par seuil. Seul le mois en cours, à Paris, compte.
// Quand une opération fait passer les deux seuils d'un coup, seul l'e-mail des 100 % part : celui des 80 % est noté comme passé.
// La vérification a ses propres transactions, juste après l'enregistrement du changement, et relit les dépenses en base :
// de deux changements simultanés, le dernier vérifié voit les deux, et aucune alerte ne manque. Si le serveur s'arrête entre
// les deux, l'alerte part au prochain changement du mois.
public class BudgetAlertService
{
    // Chaque catégorie a au plus deux alertes par mois : une vérification n'a jamais plus de passages à faire.
    public const int MaximumAlertsPerCheck = 2 * CategoryRules.MaximumCountPerUser;

    private readonly ICategoryRepository categoryRepository;
    private readonly ITransactionRepository transactionRepository;
    private readonly IAlertRepository alertRepository;
    private readonly AlertPreparation alertPreparation;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux catégories, aux opérations et aux alertes, la préparation des alertes, l'enregistrement avec réessai,
    // et l'horloge qui donne le mois en cours.
    public BudgetAlertService(
        ICategoryRepository categoryRepository,
        ITransactionRepository transactionRepository,
        IAlertRepository alertRepository,
        AlertPreparation alertPreparation,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.categoryRepository = categoryRepository;
        this.transactionRepository = transactionRepository;
        this.alertRepository = alertRepository;
        this.alertPreparation = alertPreparation;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Vérifie les budgets de l'utilisateur, et enregistre les alertes des seuils atteints ce mois-ci, avec leurs e-mails.
    // Renvoie le nombre d'e-mails mis dans la file. Chaque passage enregistre une seule alerte, dans sa propre transaction :
    // l'index unique des alertes est alors le seul verrou disputé, et deux vérifications simultanées ne s'attendent jamais
    // l'une l'autre en boucle. Une vérification que d'autres requêtes empêchent à chaque essai sera refaite au prochain changement.
    public async Task<int> CheckAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var month = AlertRules.FindBudgetMonth(UsersCalendar.FindToday(now));
        var emailCount = 0;

        for (var pass = 1; pass <= MaximumAlertsPerCheck; pass++)
        {
            var outcome = await savingService.SaveWithRetriesAsync(
                () => PrepareNextAlertAsync(userId, month, now.UtcDateTime, cancellationToken),
                PassOutcome.NothingLeft,
                cancellationToken);

            if (outcome == PassOutcome.NothingLeft)
            {
                break;
            }

            if (outcome == PassOutcome.SavedWithEmail)
            {
                emailCount = emailCount + 1;
            }
        }

        return emailCount;
    }

    // Un passage : relit les budgets, les dépenses du mois et les alertes déjà parties, puis prépare la première alerte
    // qui manque. Le seuil des 100 % passe avant celui des 80 % : l'e-mail qui compte part le premier, et les 80 %
    // sont ensuite notés sans e-mail. Une autre vérification qui enregistre la même alerte au même instant fait échouer
    // celle-ci sur l'index unique : le passage suivant relit, et passe à l'alerte suivante.
    private async Task<PreparedChanges<PassOutcome>> PrepareNextAlertAsync(Guid userId, DateOnly month, DateTime now, CancellationToken cancellationToken = default)
    {
        if (!await alertRepository.IsEnabledAsync(userId, AlertKind.BudgetReached, cancellationToken))
        {
            return PreparedChanges<PassOutcome>.NothingToSave(PassOutcome.NothingLeft);
        }

        var budgetedCategories = (await categoryRepository.FindAllAsync(userId, cancellationToken))
            .Where(category => category.MonthlyBudgetInCents != null)
            .ToList();

        if (budgetedCategories.Count == 0)
        {
            return PreparedChanges<PassOutcome>.NothingToSave(PassOutcome.NothingLeft);
        }

        var totals = await transactionRepository.SumByCategoryAndMonthAsync(userId, month, month.AddMonths(1).AddDays(-1), null, cancellationToken);
        var sentAlerts = await alertRepository.FindBudgetAlertsAsync(userId, month, cancellationToken);

        foreach (var category in budgetedCategories)
        {
            if (category.MonthlyBudgetInCents is not int budgetInCents)
            {
                continue;
            }

            var monthNetInCents = totals
                .Where(total => total.CategoryId == category.Id)
                .Sum(total => total.NetInCents);
            var reachedThreshold = AlertRules.FindReachedBudgetThreshold(monthNetInCents, budgetInCents);

            if (reachedThreshold == null)
            {
                continue;
            }

            var hasFirstAlert = HasAlert(sentAlerts, category.Id, AlertRules.FirstBudgetThreshold);
            var hasFullAlert = HasAlert(sentAlerts, category.Id, AlertRules.FullBudgetThreshold);

            if (reachedThreshold == AlertRules.FullBudgetThreshold && !hasFullAlert)
            {
                alertPreparation.PrepareWithEmail(Alert.ForBudget(userId, category.Id, month, AlertRules.FullBudgetThreshold, now));
                return PreparedChanges<PassOutcome>.SaveThenReturn(PassOutcome.SavedWithEmail);
            }

            if (!hasFirstAlert)
            {
                var firstAlert = Alert.ForBudget(userId, category.Id, month, AlertRules.FirstBudgetThreshold, now);

                // Après l'e-mail des 100 %, celui des 80 % n'apprendrait rien : il est noté comme passé, sans e-mail.
                if (hasFullAlert)
                {
                    alertPreparation.PrepareWithoutEmail(firstAlert);
                    return PreparedChanges<PassOutcome>.SaveThenReturn(PassOutcome.SavedWithoutEmail);
                }

                alertPreparation.PrepareWithEmail(firstAlert);
                return PreparedChanges<PassOutcome>.SaveThenReturn(PassOutcome.SavedWithEmail);
            }
        }

        return PreparedChanges<PassOutcome>.NothingToSave(PassOutcome.NothingLeft);
    }

    // Renvoie vrai si l'alerte de ce seuil est déjà partie pour cette catégorie.
    private static bool HasAlert(List<Alert> sentAlerts, Guid categoryId, short threshold)
    {
        return sentAlerts.Any(alert => alert.CategoryId == categoryId && alert.Threshold == threshold);
    }

    // Ce qu'un passage a enregistré.
    private enum PassOutcome
    {
        NothingLeft,       // plus aucune alerte à enregistrer, ou d'autres requêtes l'empêchent à chaque essai
        SavedWithEmail,    // une alerte, avec son e-mail
        SavedWithoutEmail  // le seuil des 80 % noté comme passé, après celui des 100 %
    }
}
