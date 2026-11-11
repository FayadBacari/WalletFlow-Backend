using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit la règle d'une suggestion en code de la colonne regle_suggestion, et inversement.
public static class SuggestionRuleDatabaseCodes
{
    // Code de la colonne regle_suggestion pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<SuggestionRule> Table =
        new EnumDatabaseCodes<SuggestionRule>(
            new Dictionary<SuggestionRule, string>
            {
                { SuggestionRule.CategoryOverrun, "depassement_categorie" },
                { SuggestionRule.UnallocatedMoney, "argent_disponible" },
                { SuggestionRule.InsufficientSavings, "epargne_insuffisante" },
            });
}
