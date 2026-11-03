using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit la règle d'une suggestion en code de la colonne regle_suggestion, et inversement.
public static class SuggestionRuleDatabaseCodes
{
    // Convertisseur utilisé par EF Core pour lire et écrire la colonne regle_suggestion.
    public static readonly ValueConverter<SuggestionRule, string> ConverterBetweenSuggestionRuleAndDatabaseCode =
        new ValueConverter<SuggestionRule, string>(
            suggestionRuleToWriteIntoTheDatabase => ConvertSuggestionRuleIntoItsDatabaseCode(suggestionRuleToWriteIntoTheDatabase),
            databaseCodeReadForTheSuggestionRule => ConvertDatabaseCodeIntoItsSuggestionRule(databaseCodeReadForTheSuggestionRule));

    // Donne le code enregistré en base pour une règle de suggestion.
    public static string ConvertSuggestionRuleIntoItsDatabaseCode(SuggestionRule suggestionRuleToConvertIntoDatabaseCode)
    {
        switch (suggestionRuleToConvertIntoDatabaseCode)
        {
            case SuggestionRule.CategoryOverrun:
                return "depassement_categorie";
            case SuggestionRule.UnallocatedMoney:
                return "argent_disponible";
            case SuggestionRule.InsufficientSavings:
                return "epargne_insuffisante";
            default:
                throw new ArgumentOutOfRangeException(nameof(suggestionRuleToConvertIntoDatabaseCode));
        }
    }

    // Donne la règle de suggestion qui correspond à un code lu en base.
    public static SuggestionRule ConvertDatabaseCodeIntoItsSuggestionRule(string databaseCodeToConvertIntoSuggestionRule)
    {
        switch (databaseCodeToConvertIntoSuggestionRule)
        {
            case "depassement_categorie":
                return SuggestionRule.CategoryOverrun;
            case "argent_disponible":
                return SuggestionRule.UnallocatedMoney;
            case "epargne_insuffisante":
                return SuggestionRule.InsufficientSavings;
            default:
                throw new ArgumentOutOfRangeException(nameof(databaseCodeToConvertIntoSuggestionRule));
        }
    }
}
