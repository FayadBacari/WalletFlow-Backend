using System.Text.Json.Serialization;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour fixer le budget mensuel d'une catégorie, en centimes, ou l'enlever avec null (GR Dépenses).
// Seul ce champ est lu (SS §4). Il doit être écrit, même à null : une requête vide n'enlève jamais un budget par erreur.
public class CategoryBudgetRequest
{
    [JsonRequired]
    public int? MonthlyBudgetInCents { get; set; }
}
