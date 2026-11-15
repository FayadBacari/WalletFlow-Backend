namespace WalletFlow.Domain.NotificationsAndSupport;

// Les 8 types d'alerte (code_type_alerte, DD §9.1). L'utilisateur peut désactiver les quatre premiers,
// jamais les quatre alertes de sécurité (GR Notif).
public enum AlertKind
{
    BudgetReached,            // budget_atteint
    NewSuggestion,            // nouvelle_suggestion
    BagGoalReached,           // objectif_sac_atteint
    BankAuthorizationToRenew, // autorisation_bancaire
    NewDevice,                // nouvel_appareil
    AccountLocked,            // compte_bloque
    PasswordChange,           // changement_mdp
    EmailChange               // changement_email
}
