using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Domain.NotificationsAndSupport;

// Règles des quatre alertes qu'on peut désactiver (GR Dépenses, GR Sacs, GR Comptes, GR Notif, DD §9.3, §11 et §12) :
// quand chacune part, et combien de temps elle est gardée. Les montants se comparent en centimes entiers, en multipliant,
// jamais en divisant : aucun arrondi caché ne fait partir une alerte trop tôt.
public static class AlertRules
{
    // Les deux seuils d'une alerte de budget, en pourcentage du budget du mois (DD §12).
    public const short FirstBudgetThreshold = 80;
    public const short FullBudgetThreshold = 100;

    // Une alerte de budget ou de suggestion est gardée pendant les 2 mois qui suivent son mois (DD §12).
    public const int KeptMonthCount = 2;

    // Le mois en cours à Paris, noté par son premier jour : seules ses dépenses déclenchent une alerte de budget.
    // Une opération d'un mois passé, arrivée en retard ou avec l'historique d'une nouvelle connexion, n'en déclenche aucune.
    public static DateOnly FindBudgetMonth(DateOnly today)
    {
        return new DateOnly(today.Year, today.Month, 1);
    }

    // Donne le plus haut seuil que les dépenses du mois atteignent : 100, 80, ou null. Le taux est arrondi vers le bas
    // (DD §11) : 80 % n'est atteint qu'une fois 80 % vraiment dépensés. Le total est net, comme dans les graphiques :
    // un remboursement réduit la dépense, et un total positif n'est pas une dépense.
    public static short? FindReachedBudgetThreshold(long monthNetInCents, int monthlyBudgetInCents)
    {
        var spentTimesHundred = ChartRules.ExpenseOf(monthNetInCents) * 100;

        if (spentTimesHundred >= (long)monthlyBudgetInCents * FullBudgetThreshold)
        {
            return FullBudgetThreshold;
        }

        if (spentTimesHundred >= (long)monthlyBudgetInCents * FirstBudgetThreshold)
        {
            return FirstBudgetThreshold;
        }

        return null;
    }

    // Renvoie vrai si le solde du sac atteint son objectif. Un sac sans objectif n'atteint rien.
    public static bool IsBagGoalReached(long balanceInCents, int? targetAmountInCents)
    {
        return targetAmountInCents is int targetInCents && balanceInCents >= targetInCents;
    }

    // Renvoie vrai si l'utilisateur doit être prévenu : l'autorisation donnée à la banque finit dans 7 jours ou moins,
    // ou elle est déjà finie sans qu'il l'ait été (GR Comptes).
    public static bool IsAuthorizationToRenew(DateTime authorizationExpiresAt, DateTime now)
    {
        return authorizationExpiresAt - now <= BankConnectionRules.RenewalWarningDelay;
    }

    // Le premier mois dont les alertes de budget et de suggestion sont gardées : celles des mois d'avant sont effacées.
    // L'alerte d'octobre est gardée en novembre et en décembre, puis effacée le 1er janvier, heure de Paris.
    public static DateOnly FindFirstKeptMonth(DateOnly today)
    {
        return FindBudgetMonth(today).AddMonths(-KeptMonthCount);
    }
}
