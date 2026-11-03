namespace WalletFlow.Domain.NotificationsAndSupport;

// Type d'alerte, parmi une liste fixe de 8 types.
// Correspond à la table type_alerte du MPD (DD §9.1).
public class AlertType
{
    // code_type_alerte : par exemple budget_atteint.
    public string Code { get; set; } = string.Empty;

    // libelle_type_alerte : nom affiché à l'utilisateur.
    public string Label { get; set; } = string.Empty;

    // est_desactivable : faux pour les alertes de sécurité.
    public bool CanBeDisabled { get; set; }
}
