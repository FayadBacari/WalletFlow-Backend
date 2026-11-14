namespace WalletFlow.Domain.BagsAndSuggestions;

// Ce qui ne va pas dans l'objectif d'un sac (voir BagRules.FindGoalProblem).
public enum BagGoalProblem
{
    // L'objectif respecte les règles, ou le sac n'en a pas.
    None,

    // Le montant à atteindre ne va pas de 1 à 1 000 000 000 centimes.
    AmountBreaksRules,

    // Une date est donnée sans montant à atteindre.
    DateWithoutAmount,

    // La date n'est pas après le jour de la saisie.
    DateTooEarly
}
