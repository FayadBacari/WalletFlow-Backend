namespace WalletFlow.Application.Suggestions;

// Résultat d'une suggestion ignorée : toujours une seule des formes écrites dans ce fichier.
public abstract class SuggestionDismissalResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private SuggestionDismissalResult()
    {
    }

    // La suggestion est ignorée : sa règle est en pause pendant 30 jours.
    public sealed class Dismissed : SuggestionDismissalResult
    {
    }

    // La suggestion n'existe pas, elle est à un autre utilisateur, ou sa catégorie a été supprimée.
    public sealed class NotFound : SuggestionDismissalResult
    {
    }

    // La suggestion a déjà été acceptée ou ignorée : elle ne change plus.
    public sealed class AlreadyAnswered : SuggestionDismissalResult
    {
    }

    // D'autres requêtes ont écrit au même instant à chaque essai, ou le compte n'existe plus : rien n'est enregistré.
    public sealed class NotSaved : SuggestionDismissalResult
    {
    }
}
