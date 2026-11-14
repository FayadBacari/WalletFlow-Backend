using WalletFlow.Application.Bags;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Suggestions;

// Cas d'utilisation : l'utilisateur connecté ignore une suggestion (GR Sacs). Elle ne réapparaît pas, et sa règle,
// avec sa catégorie pour un dépassement, ne propose plus rien pendant 30 jours. Une suggestion ignorée ne change plus.
public class SuggestionDismissalService
{
    private readonly IBagRepository bagRepository;
    private readonly ISuggestionRepository suggestionRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux sacs et aux suggestions, l'enregistrement avec réessai, et l'horloge qui date la réponse.
    public SuggestionDismissalService(
        IBagRepository bagRepository,
        ISuggestionRepository suggestionRepository,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.bagRepository = bagRepository;
        this.suggestionRepository = suggestionRepository;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Ignore la suggestion.
    public async Task<SuggestionDismissalResult> DismissAsync(Guid userId, Guid suggestionId)
    {
        return await savingService.SaveWithRetriesAsync(
            () => PrepareDismissalAsync(userId, suggestionId),
            new SuggestionDismissalResult.NotSaved());
    }

    // Un passage. Comme pour l'acceptation, la ligne de l'utilisateur est lue d'abord puis réécrite : « ignorer » et « accepter »
    // au même instant ne donnent qu'une seule réponse, et le second trouve la suggestion déjà répondue.
    private async Task<PreparedChanges<SuggestionDismissalResult>> PrepareDismissalAsync(Guid userId, Guid suggestionId)
    {
        var user = await bagRepository.FindUserAsync(userId);

        if (user == null)
        {
            return PreparedChanges<SuggestionDismissalResult>.NothingToSave(new SuggestionDismissalResult.NotSaved());
        }

        var suggestion = await suggestionRepository.FindAsync(userId, suggestionId);

        if (suggestion == null || suggestion.IsWithdrawn())
        {
            return PreparedChanges<SuggestionDismissalResult>.NothingToSave(new SuggestionDismissalResult.NotFound());
        }

        if (suggestion.Status != SuggestionStatus.Proposed)
        {
            return PreparedChanges<SuggestionDismissalResult>.NothingToSave(new SuggestionDismissalResult.AlreadyAnswered());
        }

        suggestion.Ignore(clock.GetUtcNow().UtcDateTime);
        bagRepository.PrepareUserCheck(user);

        return PreparedChanges<SuggestionDismissalResult>.SaveThenReturn(new SuggestionDismissalResult.Dismissed());
    }
}
