using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit l'état d'une suggestion en code de la colonne statut_suggestion, et inversement.
public static class SuggestionStatusDatabaseCodes
{
    // Code de la colonne statut_suggestion pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<SuggestionStatus> Table =
        new EnumDatabaseCodes<SuggestionStatus>(
            new Dictionary<SuggestionStatus, string>
            {
                { SuggestionStatus.Proposed, "proposee" },
                { SuggestionStatus.Accepted, "acceptee" },
                { SuggestionStatus.Ignored, "ignoree" },
            });
}
