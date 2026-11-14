using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Suggestions;

// Une suggestion proposée, telle que l'utilisateur la voit : la règle et le mois disent pourquoi elle apparaît, le montant
// combien elle fait économiser par mois (GR Sacs). La catégorie n'existe que pour la règle « dépassement de catégorie ».
public record SuggestionView(
    Guid Id,
    SuggestionRule Rule,
    DateOnly Month,
    Guid? CategoryId,
    string? CategoryName,
    int MonthlySavingsInCents);

// Les suggestions proposées pour le mois évalué, et ce que l'utilisateur a déjà économisé grâce aux suggestions acceptées.
public record SuggestionList(DateOnly Month, List<SuggestionView> Suggestions, long SavedInCents);
