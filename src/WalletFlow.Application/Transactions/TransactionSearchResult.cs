namespace WalletFlow.Application.Transactions;

// Résultat d'une recherche d'opérations : toujours une seule des formes écrites dans ce fichier.
public abstract class TransactionSearchResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private TransactionSearchResult()
    {
    }

    // La page demandée, et le nombre de toutes les opérations trouvées.
    public sealed class Found : TransactionSearchResult
    {
        public required TransactionPage Page { get; init; }
    }

    // Le texte cherché dépasse 255 caractères : aucun libellé ne pourrait le contenir.
    public sealed class TextTooLong : TransactionSearchResult
    {
    }

    // Le montant cherché sort des limites d'un montant (DD §5.3).
    public sealed class AmountBreaksRules : TransactionSearchResult
    {
    }

    // La date de fin est avant la date de début.
    public sealed class DatesReversed : TransactionSearchResult
    {
    }

    // Le numéro de page sort des limites.
    public sealed class PageBreaksRules : TransactionSearchResult
    {
    }
}
