using WalletFlow.Application.Bags;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Suggestions;

// Cas d'utilisation : calcule les suggestions d'épargne du mois évalué d'un utilisateur (GR Sacs). Elles portent sur le dernier
// mois complet, à Paris, et sont calculées une seule fois, à partir du 5 du mois suivant : par la tâche de fond, ou par la
// première lecture si elle arrive avant. Une opération que la banque transmet après le calcul ne les change plus.
// Au même calcul, les suggestions restées sans réponse des mois d'avant sont effacées, comme les suggestions ignorées dont
// la pause de 30 jours est finie (DD §8). Si le calcul propose au moins une suggestion, l'alerte du mois part avec lui.
public class SuggestionCalculationService
{
    // Le nombre d'utilisateurs que la tâche de fond lit à la fois.
    public const int UserBatchSize = 100;

    private readonly IBagRepository bagRepository;
    private readonly ISuggestionRepository suggestionRepository;
    private readonly ITransactionRepository transactionRepository;
    private readonly AlertPreparation alertPreparation;
    private readonly BankBalanceTotalService balanceTotalService;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux sacs, aux suggestions et aux opérations, la préparation des alertes, le calcul du solde total,
    // l'enregistrement avec réessai, et l'horloge qui donne le mois évalué.
    public SuggestionCalculationService(
        IBagRepository bagRepository,
        ISuggestionRepository suggestionRepository,
        ITransactionRepository transactionRepository,
        AlertPreparation alertPreparation,
        BankBalanceTotalService balanceTotalService,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.bagRepository = bagRepository;
        this.suggestionRepository = suggestionRepository;
        this.transactionRepository = transactionRepository;
        this.alertPreparation = alertPreparation;
        this.balanceTotalService = balanceTotalService;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Le mois évalué aujourd'hui, à Paris.
    public DateOnly FindEvaluatedMonth()
    {
        return SuggestionRules.FindEvaluatedMonth(UsersCalendar.FindToday(clock.GetUtcNow()));
    }

    // Donne, dans l'ordre de leur identifiant, au plus ce nombre d'utilisateurs après celui-ci dont ce mois attend
    // son calcul : la tâche de fond les parcourt ainsi tous, même si le calcul de l'un d'eux échoue.
    public async Task<List<Guid>> FindUsersToCalculateAsync(DateOnly month, Guid? afterUserId, int count)
    {
        return await suggestionRepository.FindUsersToCalculateAsync(month, afterUserId, count);
    }

    // Calcule les suggestions de ce mois, le mois évalué que l'appelant vient de lire, si ce n'est pas encore fait.
    // Renvoie vrai si le mois vient d'être calculé. Un calcul que d'autres requêtes empêchent à chaque essai sera refait plus tard.
    public async Task<bool> CalculateAsync(Guid userId, DateOnly month)
    {
        return await savingService.SaveWithRetriesAsync(
            () => PrepareCalculationAsync(userId, month, clock.GetUtcNow().UtcDateTime),
            false);
    }

    // Un passage du calcul. La ligne de l'utilisateur est lue d'abord, puis réécrite avec le mois calculé, en vérifiant sa version :
    // si une autre lecture calcule le même mois au même instant, un seul des deux enregistrements passe, et l'autre trouve
    // le mois déjà noté. Un déplacement d'argent enregistré entre-temps fait aussi refaire le calcul, avec l'argent disponible à jour.
    private async Task<PreparedChanges<bool>> PrepareCalculationAsync(Guid userId, DateOnly month, DateTime now)
    {
        var user = await bagRepository.FindUserAsync(userId);

        if (user == null || (user.LastSuggestionMonth is DateOnly notedMonth && notedMonth >= month))
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        // Sans aucune opération, rien ne peut être évalué : le mois sera calculé quand la banque aura donné ses opérations.
        var earliestDate = await transactionRepository.FindEarliestDateAsync(userId, null);

        if (earliestDate is not DateOnly firstDate)
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        var suggestions = await suggestionRepository.FindAllAsync(userId);
        var pausedSuggestions = new List<SavingsSuggestion>();

        foreach (var suggestion in suggestions)
        {
            if (suggestion.Status == SuggestionStatus.Proposed && suggestion.Month < month)
            {
                suggestionRepository.PrepareDeletion(suggestion);
            }
            else if (suggestion.Status == SuggestionStatus.Ignored && suggestion.StatusChangedAt is DateTime ignoredAt)
            {
                if (SuggestionRules.IsPaused(ignoredAt, now))
                {
                    pausedSuggestions.Add(suggestion);
                }
                else
                {
                    suggestionRepository.PrepareDeletion(suggestion);
                }
            }
        }

        var newSuggestions = await FindNewSuggestionsAsync(userId, month, firstDate);
        var proposedCount = 0;

        foreach (var suggestion in newSuggestions)
        {
            var isPaused = pausedSuggestions.Any(paused => paused.Rule == suggestion.Rule && paused.CategoryId == suggestion.CategoryId);
            var alreadyExists = suggestions.Any(existing =>
                existing.Rule == suggestion.Rule
                && existing.CategoryId == suggestion.CategoryId
                && existing.Month == suggestion.Month);

            if (!isPaused && !alreadyExists)
            {
                suggestionRepository.PrepareSuggestion(suggestion);
                proposedCount = proposedCount + 1;
            }
        }

        // Une seule alerte par mois, quand le calcul propose au moins une suggestion (GR Sacs). Le mois n'est calculé
        // qu'une fois : de deux calculs simultanés, le second bute sur l'index unique de l'alerte, relit, et trouve le mois noté.
        if (proposedCount > 0)
        {
            await alertPreparation.TryPrepareAsync(Alert.ForSuggestions(userId, month, now));
        }

        user.NoteSuggestionMonth(month);

        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Applique les trois règles au mois évalué (GR Sacs). Le dépassement d'une catégorie demande 3 mois complets d'historique
    // avant ce mois, et l'épargne du mois demande que tout le mois soit connu : la première opération date de son premier jour
    // ou d'avant. L'argent disponible est celui de ce moment.
    private async Task<List<SavingsSuggestion>> FindNewSuggestionsAsync(Guid userId, DateOnly month, DateOnly firstDate)
    {
        var averageStart = month.AddMonths(-ChartRules.AverageMonthCount);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var totals = await transactionRepository.SumByCategoryAndMonthAsync(userId, averageStart, monthEnd, null);
        var suggestions = new List<SavingsSuggestion>();

        if (firstDate <= averageStart)
        {
            foreach (var categoryId in totals.Select(total => total.CategoryId).Distinct())
            {
                var monthNetInCents = SumNet(totals, categoryId, month, true);
                var previousMonthsNetInCents = SumNet(totals, categoryId, month, false);

                if (SuggestionRules.FindCategoryOverrunSavings(monthNetInCents, previousMonthsNetInCents) is int overrunSavings)
                {
                    suggestions.Add(CreateSuggestion(userId, SuggestionRule.CategoryOverrun, month, overrunSavings, categoryId));
                }
            }
        }

        var balances = await bagRepository.SumBalancesAsync(userId);
        var bankTotal = await balanceTotalService.ReadAsync(userId);

        if (SuggestionRules.FindUnallocatedMoneySavings(bankTotal.TotalInCents - balances.Values.Sum()) is int unallocatedSavings)
        {
            suggestions.Add(CreateSuggestion(userId, SuggestionRule.UnallocatedMoney, month, unallocatedSavings, null));
        }

        if (firstDate <= month)
        {
            long incomeInCents = 0;
            long expenseInCents = 0;

            foreach (var total in totals.Where(total => total.Year == month.Year && total.Month == month.Month))
            {
                incomeInCents = incomeInCents + ChartRules.IncomeOf(total.NetInCents);
                expenseInCents = expenseInCents + ChartRules.ExpenseOf(total.NetInCents);
            }

            if (SuggestionRules.FindMissingSavings(incomeInCents, expenseInCents) is int missingSavings)
            {
                suggestions.Add(CreateSuggestion(userId, SuggestionRule.InsufficientSavings, month, missingSavings, null));
            }
        }

        return suggestions;
    }

    // Additionne le total net de la catégorie sur le mois évalué, ou sur les 3 mois d'avant.
    private static long SumNet(List<CategoryMonthTotal> totals, Guid categoryId, DateOnly month, bool isEvaluatedMonth)
    {
        return totals
            .Where(total =>
                total.CategoryId == categoryId
                && (total.Year == month.Year && total.Month == month.Month) == isEvaluatedMonth)
            .Sum(total => total.NetInCents);
    }

    // Crée une suggestion proposée de l'utilisateur.
    private static SavingsSuggestion CreateSuggestion(Guid userId, SuggestionRule rule, DateOnly month, int savingsInCents, Guid? categoryId)
    {
        return new SavingsSuggestion
        {
            Rule = rule,
            Month = month,
            MonthlySavingsInCents = savingsInCents,
            UserId = userId,
            CategoryId = categoryId
        };
    }
}
