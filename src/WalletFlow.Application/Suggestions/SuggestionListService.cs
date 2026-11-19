using WalletFlow.Application.Categories;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Suggestions;

// Cas d'utilisation : l'utilisateur connecté voit ses suggestions d'épargne et ce qu'il a économisé (GR Sacs).
// Les suggestions portent sur le dernier mois complet, à Paris. La tâche horaire les calcule, à partir du 5 du mois suivant
// (voir SuggestionCalculationService) : la lecture ne fait que lire, comme toute requête GET (SS §6). Avant le passage de la
// tâche, la liste du nouveau mois est vide.
public class SuggestionListService
{
    private readonly SuggestionCalculationService calculationService;
    private readonly ISuggestionRepository suggestionRepository;
    private readonly ICategoryRepository categoryRepository;

    // Reçoit le calcul des suggestions, pour le mois évalué, et l'accès aux suggestions et aux catégories.
    public SuggestionListService(
        SuggestionCalculationService calculationService,
        ISuggestionRepository suggestionRepository,
        ICategoryRepository categoryRepository)
    {
        this.calculationService = calculationService;
        this.suggestionRepository = suggestionRepository;
        this.categoryRepository = categoryRepository;
    }

    // Donne les suggestions du mois évalué encore sans réponse, les plus grosses économies d'abord. Rien n'est écrit.
    public async Task<SuggestionList> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var month = calculationService.FindEvaluatedMonth();
        var suggestions = await suggestionRepository.FindAllAsync(userId, cancellationToken);
        var categories = await categoryRepository.FindAllAsync(userId, cancellationToken);
        var views = new List<SuggestionView>();

        foreach (var suggestion in suggestions)
        {
            if (suggestion.Status != SuggestionStatus.Proposed || suggestion.Month != month || suggestion.IsWithdrawn())
            {
                continue;
            }

            var categoryName = categories.SingleOrDefault(category => category.Id == suggestion.CategoryId)?.Name;
            views.Add(new SuggestionView(
                suggestion.Id,
                suggestion.Rule,
                suggestion.Month,
                suggestion.CategoryId,
                categoryName,
                suggestion.MonthlySavingsInCents));
        }

        views = views
            .OrderByDescending(view => view.MonthlySavingsInCents)
            .ThenBy(view => view.Id)
            .ToList();

        return new SuggestionList(month, views, await suggestionRepository.SumSavedAsync(userId, cancellationToken));
    }
}
