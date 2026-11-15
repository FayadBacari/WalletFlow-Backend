using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit le type d'une alerte en code de la colonne code_type_alerte, et inversement. Ce sont les codes de la table
// type_alerte, que le script SQL remplit.
public static class AlertKindDatabaseCodes
{
    // Code de la colonne code_type_alerte pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<AlertKind> Table =
        new EnumDatabaseCodes<AlertKind>(
            new Dictionary<AlertKind, string>
            {
                { AlertKind.BudgetReached, "budget_atteint" },
                { AlertKind.NewSuggestion, "nouvelle_suggestion" },
                { AlertKind.BagGoalReached, "objectif_sac_atteint" },
                { AlertKind.BankAuthorizationToRenew, "autorisation_bancaire" },
                { AlertKind.NewDevice, "nouvel_appareil" },
                { AlertKind.AccountLocked, "compte_bloque" },
                { AlertKind.PasswordChange, "changement_mdp" },
                { AlertKind.EmailChange, "changement_email" },
            });
}
