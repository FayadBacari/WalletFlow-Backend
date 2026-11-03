namespace WalletFlow.Domain.BagsAndSuggestions;

// Suggestion d'épargne proposée par l'application.
// Correspond à la table suggestion du MPD (DD §8). Les dates sont en UTC.
public class SavingsSuggestion
{
    // id_suggestion
    public Guid Id { get; set; } = Guid.NewGuid();

    // regle_suggestion
    public SuggestionRule Rule { get; set; }

    // economie_mensuelle : économie estimée par mois, en centimes.
    public int MonthlySavingsInCents { get; set; }

    // statut_suggestion : proposée par défaut.
    public SuggestionStatus Status { get; set; } = SuggestionStatus.Proposed;

    // date_statut_suggestion : une suggestion ignorée ne réapparaît pas pendant 30 jours.
    public DateTime? StatusChangedAt { get; set; }

    // id_utilisateur
    public Guid UserId { get; set; }

    // id_categorie : seulement pour la règle « dépassement de catégorie ».
    public Guid? CategoryId { get; set; }
}
