namespace WalletFlow.Api.Contracts;

// Une suggestion proposée (GR Sacs). Rule vaut « categoryOverrun », « unallocatedMoney » ou « insufficientSavings » : avec le mois
// et la catégorie, elle dit pourquoi la suggestion apparaît. Le montant est ce qu'elle fait économiser par mois, en centimes.
// Le nom de la catégorie est du contenu non fiable : le site l'affiche toujours comme du texte, jamais comme du HTML (SS §5).
public record SuggestionResponse(
    Guid Id,
    string Rule,
    DateOnly Month,
    Guid? CategoryId,
    string? CategoryName,
    int MonthlySavingsInCents);

// Les suggestions du dernier mois complet, les plus grosses économies d'abord, et ce que l'utilisateur a déjà économisé
// grâce aux suggestions acceptées, en centimes (DD §11).
public record SuggestionListResponse(DateOnly Month, List<SuggestionResponse> Suggestions, long SavedInCents);
