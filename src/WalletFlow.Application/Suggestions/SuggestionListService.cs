using WalletFlow.Application.Categories;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Suggestions;

// Cas d'utilisation : l'utilisateur connecté voit ses suggestions d'épargne et ce qu'il a économisé (GR Sacs).
// Les suggestions portent sur le dernier mois complet, à Paris. Si la tâche de fond ne les a pas encore calculées,
// la première lecture à partir du 5 du mois suivant le fait (voir SuggestionCalculationService). Elles sont ensuite lues telles quelles.
public class SuggestionListService
{
    private readonly SuggestionCalculationService calculationService;
    private readonly ISuggestionRepository suggestionRepository;
    private readonly ICategoryRepository categoryRepository;

    // Reçoit le calcul des suggestions, et l'accès aux suggestions et aux catégories.
    public SuggestionListService(
        SuggestionCalculationService calculationService,
        ISuggestionRepository suggestionRepository,
        ICategoryRepository categoryRepository)
    {
        this.calculationService = calculationService;
        this.suggestionRepository = suggestionRepository;
        this.categoryRepository = categoryRepository;
    }

    // Calcule les suggestions du mois évalué si ce n'est pas encore fait, puis les donne, les plus grosses économies d'abord.
    // Un calcul que d'autres requêtes empêchent à chaque essai sera refait plus tard : la liste reste lisible.
    public async Task<SuggestionList> ListAsync(Guid userId)
    {
        var month = calculationService.FindEvaluatedMonth();

        await calculationService.CalculateAsync(userId, month);

        var suggestions = await suggestionRepository.FindAllAsync(userId);
        var categories = await categoryRepository.FindAllAsync(userId);
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

        return new SuggestionList(month, views, await suggestionRepository.SumSavedAsync(userId));
    }
}
