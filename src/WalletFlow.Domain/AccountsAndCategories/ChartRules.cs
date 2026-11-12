namespace WalletFlow.Domain.AccountsAndCategories;

// Règles des graphiques (GR Dépenses, DD §11). Les transferts n'y comptent jamais (GR Comptes) : les requêtes les écartent.
// Chaque catégorie compte pour son total net : un remboursement réduit ses dépenses. Un total négatif est une dépense,
// un total positif un revenu : l'épargne d'un mois vaut exactement l'argent entré moins l'argent sorti.
public static class ChartRules
{
    // Le fuseau horaire des utilisateurs, en France : le mois en cours commence à minuit, heure de Paris (GR Dépenses).
    public const string UsersTimeZoneId = "Europe/Paris";

    // Une période couvre au plus 10 ans (SS §6) : le graphique mois par mois a donc 120 barres au plus.
    public const int MaximumPeriodMonths = 120;

    // La moyenne porte sur les 3 mois complets avant le mois en cours (DD §11).
    public const int AverageMonthCount = 3;

    // Donne le jour d'aujourd'hui à Paris.
    public static DateOnly FindUsersToday(DateTimeOffset now)
    {
        var usersTimeZone = TimeZoneInfo.FindSystemTimeZoneById(UsersTimeZoneId);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, usersTimeZone).DateTime);
    }

    // Compte les mois civils que touche une période, le premier et le dernier compris.
    public static int CountMonths(DateOnly from, DateOnly to)
    {
        return (to.Year - from.Year) * 12 + to.Month - from.Month + 1;
    }

    // Renvoie vrai si la période touche plus de 120 mois civils.
    public static bool IsPeriodTooLong(DateOnly from, DateOnly to)
    {
        return CountMonths(from, to) > MaximumPeriodMonths;
    }

    // La dépense d'une catégorie dont le total net est négatif ; zéro sinon.
    public static long ExpenseOf(long netInCents)
    {
        if (netInCents < 0)
        {
            return -netInCents;
        }

        return 0;
    }

    // Le revenu d'une catégorie dont le total net est positif ; zéro sinon.
    public static long IncomeOf(long netInCents)
    {
        if (netInCents > 0)
        {
            return netInCents;
        }

        return 0;
    }

    // Une moyenne par mois, arrondie au centime le plus proche ; un demi-centime s'éloigne de zéro.
    public static long AverageOf(long totalInCents, int monthCount)
    {
        return (long)Math.Round((decimal)totalInCents / monthCount, MidpointRounding.AwayFromZero);
    }

    // Le taux d'utilisation d'un budget, en pour cent (DD §11), arrondi vers le bas : 80 % n'est atteint qu'une fois 80 %
    // vraiment dépensés. Renvoie null pour une catégorie sans budget. Un budget de quelques centimes peut donner un taux
    // immense : il s'arrête au plus grand entier, au lieu de déborder en un nombre faux.
    public static int? CountBudgetUsedPercent(long spentInCents, int? monthlyBudgetInCents)
    {
        if (monthlyBudgetInCents is not int budget)
        {
            return null;
        }

        var percent = spentInCents * 100 / budget;

        if (percent > int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)percent;
    }
}
