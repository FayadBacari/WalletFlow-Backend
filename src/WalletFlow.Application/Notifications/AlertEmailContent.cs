namespace WalletFlow.Application.Notifications;

// Ce que dit l'e-mail d'une alerte, lu au moment de l'envoi : toujours une seule des formes écrites dans ce fichier.
// Aucune ne contient de montant (GR Notif, SS §9). Les noms viennent de l'utilisateur ou de la banque : c'est du texte.
public abstract class AlertEmailContent
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AlertEmailContent()
    {
    }

    // Les dépenses du mois dans une catégorie atteignent un seuil de son budget.
    public sealed class BudgetReached : AlertEmailContent
    {
        public required string CategoryName { get; init; }
        public required DateOnly Month { get; init; }
        public required short Threshold { get; init; }
    }

    // Le calcul du mois propose de nouvelles suggestions d'épargne.
    public sealed class NewSuggestions : AlertEmailContent
    {
        public required DateOnly Month { get; init; }
    }

    // Un sac atteint son objectif.
    public sealed class BagGoalReached : AlertEmailContent
    {
        public required string BagName { get; init; }
    }

    // L'autorisation donnée à une banque est à renouveler.
    public sealed class BankAuthorizationToRenew : AlertEmailContent
    {
        public required string BankName { get; init; }
        public required DateTime AuthorizationExpiresAt { get; init; }
    }
}
