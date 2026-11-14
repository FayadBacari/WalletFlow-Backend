using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Résultat de la création d'un sac : toujours une seule des formes écrites dans ce fichier.
public abstract class BagCreationResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BagCreationResult()
    {
    }

    // Le sac est créé, vide.
    public sealed class Created : BagCreationResult
    {
        public required BagView Bag { get; init; }
    }

    // Le nom ne respecte pas les règles.
    public sealed class NameBreaksRules : BagCreationResult
    {
    }

    // L'objectif ne respecte pas les règles, pour cette raison.
    public sealed class GoalBreaksRules : BagCreationResult
    {
        public required BagGoalProblem Problem { get; init; }
    }

    // L'utilisateur a déjà le nombre maximum de sacs.
    public sealed class TooManyBags : BagCreationResult
    {
    }

    // D'autres requêtes changent les sacs sans arrêt, ou le compte n'existe plus : rien n'est enregistré.
    public sealed class NotSaved : BagCreationResult
    {
    }
}
