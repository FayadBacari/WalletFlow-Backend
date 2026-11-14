using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Domain.BagsAndSuggestions;

// Règles fixes des suggestions d'épargne (GR Sacs, DD §8 et §12) : le mois évalué, ce que chaque règle fait économiser
// par mois, et la pause de 30 jours après une suggestion ignorée. Chaque calcul se fait en centimes entiers, sans arrondi
// caché : un seuil de 20 % se compare en multipliant, jamais en divisant.
public static class SuggestionRules
{
    public const int OverrunPercent = 20;
    public const int SavingsRatePercent = 20;
    public const int MaximumSavingsInCents = 1_000_000_000;

    // La pause après une suggestion ignorée : 30 jours, comptés à partir de la réponse.
    public static readonly TimeSpan IgnoredPause = TimeSpan.FromDays(30);

    // Le mois évalué : le dernier mois complet, c'est-à-dire le mois d'avant celui de ce jour, à Paris.
    public static DateOnly FindEvaluatedMonth(DateOnly today)
    {
        return new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
    }

    // Règle 1 : la catégorie a dépensé ce mois-ci plus de 20 % au-dessus de sa moyenne des 3 mois d'avant.
    // Les totaux sont nets, comme dans les graphiques (GR Dépenses) : négatifs pour une dépense.
    // Seule une catégorie dont la moyenne est une dépense compte. L'économie est ce qui dépasse la moyenne.
    // Renvoie null quand la règle ne se déclenche pas.
    public static int? FindCategoryOverrunSavings(long monthNetInCents, long previousMonthsNetInCents)
    {
        var monthExpenseInCents = ChartRules.ExpenseOf(monthNetInCents);
        var previousExpenseInCents = ChartRules.ExpenseOf(previousMonthsNetInCents);

        if (previousExpenseInCents == 0)
        {
            return null;
        }

        // dépense > moyenne × 1,2, avec moyenne = total ÷ 3 : dépense × 3 × 100 > total × 120, sans division.
        var monthCount = ChartRules.AverageMonthCount;
        if (monthExpenseInCents * monthCount * 100 <= previousExpenseInCents * (100 + OverrunPercent))
        {
            return null;
        }

        var savingsInCents = monthExpenseInCents - ChartRules.AverageOf(previousExpenseInCents, monthCount);

        return Limit(savingsInCents);
    }

    // Règle 2 : de l'argent disponible reste hors des sacs. L'économie est tout l'argent disponible.
    // Renvoie null quand il n'en reste pas.
    public static int? FindUnallocatedMoneySavings(long availableInCents)
    {
        return Limit(availableInCents);
    }

    // Règle 3 : l'épargne du mois (revenus moins dépenses) est sous 20 % des revenus, pour un mois qui a des revenus.
    // L'économie est ce qui manque pour atteindre 20 %, arrondi au centime du dessus : la placer suffit à les atteindre.
    // Renvoie null quand la règle ne se déclenche pas.
    public static int? FindMissingSavings(long incomeInCents, long expenseInCents)
    {
        if (incomeInCents <= 0)
        {
            return null;
        }

        var savingsInCents = incomeInCents - expenseInCents;
        var missingTimesHundred = incomeInCents * SavingsRatePercent - savingsInCents * 100;

        if (missingTimesHundred <= 0)
        {
            return null;
        }

        return Limit((missingTimesHundred + 99) / 100);
    }

    // Renvoie vrai si une suggestion ignorée à cette date met encore sa règle en pause : pendant 30 jours exactement.
    public static bool IsPaused(DateTime ignoredAt, DateTime now)
    {
        return now < ignoredAt + IgnoredPause;
    }

    // Une économie va de 1 centime à 1 000 000 000 centimes (DD §8) : moins d'un centime ne se propose pas,
    // et une somme plus grande s'arrête au maximum.
    private static int? Limit(long savingsInCents)
    {
        if (savingsInCents < 1)
        {
            return null;
        }

        if (savingsInCents > MaximumSavingsInCents)
        {
            return MaximumSavingsInCents;
        }

        return (int)savingsInCents;
    }
}
