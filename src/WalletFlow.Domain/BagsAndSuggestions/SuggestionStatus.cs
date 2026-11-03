namespace WalletFlow.Domain.BagsAndSuggestions;

// État d'une suggestion d'épargne (statut_suggestion, DD §8).
public enum SuggestionStatus
{
    Proposed, // proposee
    Accepted, // acceptee
    Ignored   // ignoree
}
