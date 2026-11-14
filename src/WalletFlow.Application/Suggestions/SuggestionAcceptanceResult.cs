using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Suggestions;

// Résultat de l'acceptation d'une suggestion : toujours une seule des formes écrites dans ce fichier.
public abstract class SuggestionAcceptanceResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private SuggestionAcceptanceResult()
    {
    }

    // La suggestion est acceptée, et son montant est placé dans le sac par ce mouvement.
    public sealed class Accepted : SuggestionAcceptanceResult
    {
        public required BagMovement Movement { get; init; }
    }

    // La suggestion n'existe pas, elle est à un autre utilisateur, ou sa catégorie a été supprimée.
    public sealed class NotFound : SuggestionAcceptanceResult
    {
    }

    // La suggestion a déjà été acceptée ou ignorée : elle ne change plus.
    public sealed class AlreadyAnswered : SuggestionAcceptanceResult
    {
    }

    // Le montant ne va pas de 1 centime au montant de la suggestion.
    public sealed class AmountBreaksRules : SuggestionAcceptanceResult
    {
    }

    // Le sac n'existe pas, ou il est à un autre utilisateur.
    public sealed class BagNotFound : SuggestionAcceptanceResult
    {
    }

    // L'utilisateur a déjà fait le nombre maximum de déplacements sur les dernières 24 heures.
    public sealed class TooManyMovements : SuggestionAcceptanceResult
    {
    }

    // Le montant porterait le total des sacs au-dessus du solde total : l'argent disponible ne suffit pas.
    public sealed class AvailableMoneyTooLow : SuggestionAcceptanceResult
    {
    }

    // D'autres requêtes ont écrit au même instant à chaque essai, ou le compte n'existe plus : rien n'est enregistré.
    public sealed class NotSaved : SuggestionAcceptanceResult
    {
    }
}
