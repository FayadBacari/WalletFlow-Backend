using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit l'état d'une suggestion en code de la colonne statut_suggestion, et inversement.
public static class SuggestionStatusDatabaseCodes
{
    // Convertisseur utilisé par EF Core pour lire et écrire la colonne statut_suggestion.
    public static readonly ValueConverter<SuggestionStatus, string> ConverterBetweenSuggestionStatusAndDatabaseCode =
        new ValueConverter<SuggestionStatus, string>(
            suggestionStatusToWriteIntoTheDatabase => ConvertSuggestionStatusIntoItsDatabaseCode(suggestionStatusToWriteIntoTheDatabase),
            databaseCodeReadForTheSuggestionStatus => ConvertDatabaseCodeIntoItsSuggestionStatus(databaseCodeReadForTheSuggestionStatus));

    // Donne le code enregistré en base pour un état de suggestion.
    public static string ConvertSuggestionStatusIntoItsDatabaseCode(SuggestionStatus suggestionStatusToConvertIntoDatabaseCode)
    {
        switch (suggestionStatusToConvertIntoDatabaseCode)
        {
            case SuggestionStatus.Proposed:
                return "proposee";
            case SuggestionStatus.Accepted:
                return "acceptee";
            case SuggestionStatus.Ignored:
                return "ignoree";
            default:
                throw new ArgumentOutOfRangeException(nameof(suggestionStatusToConvertIntoDatabaseCode));
        }
    }

    // Donne l'état de suggestion qui correspond à un code lu en base.
    public static SuggestionStatus ConvertDatabaseCodeIntoItsSuggestionStatus(string databaseCodeToConvertIntoSuggestionStatus)
    {
        switch (databaseCodeToConvertIntoSuggestionStatus)
        {
            case "proposee":
                return SuggestionStatus.Proposed;
            case "acceptee":
                return SuggestionStatus.Accepted;
            case "ignoree":
                return SuggestionStatus.Ignored;
            default:
                throw new ArgumentOutOfRangeException(nameof(databaseCodeToConvertIntoSuggestionStatus));
        }
    }
}
