namespace WalletFlow.Domain.NotificationsAndSupport;

// Une alerte envoyée, d'un des quatre types qu'on peut désactiver : elle est gardée pour ne jamais envoyer deux fois
// la même (GR Notif). Correspond à la table alerte du MPD (DD §9.3). Les dates sont en UTC.
// Chaque type se crée par sa propre méthode, avec exactement ce dont il a besoin : une catégorie, un mois et un seuil
// pour le budget, un mois pour les suggestions, un sac, ou une connexion bancaire.
public class Alert
{
    // id_alerte
    public Guid Id { get; init; } = Guid.NewGuid();

    // date_alerte : donnée par l'horloge de l'application.
    public required DateTime CreatedAt { get; init; }

    // mois_alerte : le premier jour du mois des dépenses ou des suggestions, seulement pour ces deux types.
    public DateOnly? Month { get; init; }

    // seuil_alerte : 80 ou 100, seulement pour une alerte de budget.
    public short? Threshold { get; init; }

    // id_utilisateur
    public Guid UserId { get; init; }

    // code_type_alerte : un des quatre types qu'on peut désactiver.
    public AlertKind Kind { get; init; }

    // id_categorie : seulement pour une alerte de budget.
    public Guid? CategoryId { get; init; }

    // id_sac : seulement pour une alerte d'objectif de sac.
    public Guid? BagId { get; init; }

    // id_connexion : seulement pour une alerte d'autorisation bancaire à renouveler.
    public Guid? ConnectionId { get; init; }

    // Les dépenses du mois dans cette catégorie atteignent ce seuil de son budget (GR Dépenses).
    public static Alert ForBudget(Guid userId, Guid categoryId, DateOnly month, short threshold, DateTime now)
    {
        if (threshold != AlertRules.FirstBudgetThreshold && threshold != AlertRules.FullBudgetThreshold)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Un seuil de budget vaut 80 ou 100.");
        }

        return new Alert
        {
            Kind = AlertKind.BudgetReached,
            UserId = userId,
            CategoryId = categoryId,
            Month = ReadFirstDay(month),
            Threshold = threshold,
            CreatedAt = now
        };
    }

    // Le calcul des suggestions de ce mois en propose au moins une (GR Sacs).
    public static Alert ForSuggestions(Guid userId, DateOnly month, DateTime now)
    {
        return new Alert { Kind = AlertKind.NewSuggestion, UserId = userId, Month = ReadFirstDay(month), CreatedAt = now };
    }

    // Ce sac atteint son objectif (GR Sacs).
    public static Alert ForBagGoal(Guid userId, Guid bagId, DateTime now)
    {
        return new Alert { Kind = AlertKind.BagGoalReached, UserId = userId, BagId = bagId, CreatedAt = now };
    }

    // L'autorisation donnée à la banque de cette connexion est à renouveler (GR Comptes).
    public static Alert ForBankAuthorization(Guid userId, Guid connectionId, DateTime now)
    {
        return new Alert { Kind = AlertKind.BankAuthorizationToRenew, UserId = userId, ConnectionId = connectionId, CreatedAt = now };
    }

    // Un mois est noté par son premier jour : un autre jour est refusé.
    private static DateOnly ReadFirstDay(DateOnly month)
    {
        if (month.Day != 1)
        {
            throw new ArgumentException("Un mois est noté par son premier jour.", nameof(month));
        }

        return month;
    }
}
