using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Résultat du changement de l'objectif d'un sac : toujours une seule des formes écrites dans ce fichier.
public abstract class BagGoalResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BagGoalResult()
    {
    }

    // Le sac a son nouvel objectif, ou n'en a plus.
    public sealed class Changed : BagGoalResult
    {
        public required BagView Bag { get; init; }
    }

    // L'objectif ne respecte pas les règles, pour cette raison.
    public sealed class GoalBreaksRules : BagGoalResult
    {
        public required BagGoalProblem Problem { get; init; }
    }

    // Le sac n'existe pas, ou il est à un autre utilisateur.
    public sealed class NotFound : BagGoalResult
    {
    }

    // D'autres requêtes changent ce sac sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : BagGoalResult
    {
    }
}
