namespace WalletFlow.Domain.BagsAndSuggestions;

// Règle qui a déclenché une suggestion d'épargne (regle_suggestion, DD §8).
public enum SuggestionRule
{
    CategoryOverrun,    // depassement_categorie : plus de 20 % au-dessus de la moyenne sur 3 mois
    UnallocatedMoney,   // argent_disponible : de l'argent reste hors des sacs en fin de mois
    InsufficientSavings // epargne_insuffisante : épargne du mois sous 20 % des revenus
}
