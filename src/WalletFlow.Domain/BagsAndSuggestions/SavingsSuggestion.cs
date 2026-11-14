namespace WalletFlow.Domain.BagsAndSuggestions;

// Suggestion d'épargne proposée par l'application.
// Correspond à la table suggestion du MPD (DD §8). Les dates sont en UTC.
// Une suggestion naît proposée. Elle peut ensuite être acceptée ou ignorée, une seule fois : ces deux états ne changent plus (GR Sacs).
public class SavingsSuggestion
{
    private SuggestionStatus status = SuggestionStatus.Proposed;
    private DateTime? statusChangedAt = null;

    // id_suggestion
    public Guid Id { get; init; } = Guid.NewGuid();

    // regle_suggestion
    public SuggestionRule Rule { get; init; }

    // mois_suggestion : premier jour du mois évalué, le dernier mois complet à Paris.
    public DateOnly Month { get; init; }

    // economie_mensuelle : économie estimée par mois, en centimes, de 1 à 1 000 000 000.
    public int MonthlySavingsInCents { get; init; }

    // statut_suggestion : proposée par défaut.
    public SuggestionStatus Status
    {
        get => status;
        init => status = value;
    }

    // date_statut_suggestion : la date de la réponse. Une suggestion ignorée met sa règle en pause pendant 30 jours.
    public DateTime? StatusChangedAt
    {
        get => statusChangedAt;
        init => statusChangedAt = value;
    }

    // id_utilisateur
    public Guid UserId { get; init; }

    // id_categorie : seulement pour la règle « dépassement de catégorie ». Vide quand la catégorie a été supprimée.
    public Guid? CategoryId { get; init; }

    // Renvoie vrai si la suggestion est retirée : encore proposée, mais sa catégorie a été supprimée (GR Sacs).
    // L'effacement de la catégorie l'efface aussi ; ce test couvre une suggestion enregistrée au même instant.
    public bool IsWithdrawn()
    {
        return status == SuggestionStatus.Proposed && Rule == SuggestionRule.CategoryOverrun && CategoryId == null;
    }

    // Note que l'utilisateur a accepté la suggestion : son montant, ou un montant plus petit, va dans un sac (GR Sacs).
    public void Accept(DateTime now)
    {
        Answer(SuggestionStatus.Accepted, now);
    }

    // Note que l'utilisateur a ignoré la suggestion (GR Sacs).
    public void Ignore(DateTime now)
    {
        Answer(SuggestionStatus.Ignored, now);
    }

    // Donne la réponse de l'utilisateur. Une suggestion déjà acceptée ou ignorée ne change plus : une seconde réponse est refusée.
    private void Answer(SuggestionStatus answer, DateTime now)
    {
        if (status != SuggestionStatus.Proposed)
        {
            throw new InvalidOperationException("La suggestion a déjà reçu une réponse : elle ne change plus.");
        }

        status = answer;
        statusChangedAt = now;
    }
}
