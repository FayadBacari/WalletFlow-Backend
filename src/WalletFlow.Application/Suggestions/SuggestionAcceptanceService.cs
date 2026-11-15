using WalletFlow.Application.Bags;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Suggestions;

// Cas d'utilisation : l'utilisateur connecté accepte une suggestion (GR Sacs). Son montant, ou un montant plus petit,
// part de l'argent disponible vers un sac qu'il choisit, et la suggestion devient acceptée : les deux ensemble, ou rien.
// Toutes les règles d'un déplacement s'appliquent. Une suggestion ne crée qu'un seul mouvement, même après un double clic (SS §5).
// Un sac qui atteint son objectif grâce à ce montant envoie son alerte avec lui.
public class SuggestionAcceptanceService
{
    private readonly IBagRepository bagRepository;
    private readonly ISuggestionRepository suggestionRepository;
    private readonly BagMovementChecks movementChecks;
    private readonly BagGoalAlerts goalAlerts;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux sacs et aux suggestions, les contrôles d'un déplacement, l'alerte d'objectif, l'enregistrement
    // avec réessai, et l'horloge.
    public SuggestionAcceptanceService(
        IBagRepository bagRepository,
        ISuggestionRepository suggestionRepository,
        BagMovementChecks movementChecks,
        BagGoalAlerts goalAlerts,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.bagRepository = bagRepository;
        this.suggestionRepository = suggestionRepository;
        this.movementChecks = movementChecks;
        this.goalAlerts = goalAlerts;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Accepte la suggestion en plaçant ce montant dans ce sac.
    public async Task<SuggestionAcceptanceResult> AcceptAsync(Guid userId, Guid suggestionId, Guid bagId, int amountInCents)
    {
        if (!BagRules.IsAmountValid(amountInCents))
        {
            return new SuggestionAcceptanceResult.AmountBreaksRules();
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareAcceptanceAsync(userId, suggestionId, bagId, amountInCents),
            new SuggestionAcceptanceResult.NotSaved());
    }

    // Un passage. La ligne de l'utilisateur est lue d'abord, puis réécrite avec le mouvement et la réponse : une autre réponse
    // ou un autre déplacement enregistré entre-temps fait échouer cet enregistrement, et le passage suivant relit tout.
    // Deux clics sur « accepter », ou « accepter » et « ignorer » au même instant, ne donnent donc qu'une seule réponse.
    private async Task<PreparedChanges<SuggestionAcceptanceResult>> PrepareAcceptanceAsync(
        Guid userId,
        Guid suggestionId,
        Guid bagId,
        int amountInCents)
    {
        var user = await bagRepository.FindUserAsync(userId);

        if (user == null)
        {
            return PreparedChanges<SuggestionAcceptanceResult>.NothingToSave(new SuggestionAcceptanceResult.NotSaved());
        }

        var suggestion = await suggestionRepository.FindAsync(userId, suggestionId);

        if (suggestion == null || suggestion.IsWithdrawn())
        {
            return PreparedChanges<SuggestionAcceptanceResult>.NothingToSave(new SuggestionAcceptanceResult.NotFound());
        }

        if (suggestion.Status != SuggestionStatus.Proposed)
        {
            return PreparedChanges<SuggestionAcceptanceResult>.NothingToSave(new SuggestionAcceptanceResult.AlreadyAnswered());
        }

        if (amountInCents > suggestion.MonthlySavingsInCents)
        {
            return PreparedChanges<SuggestionAcceptanceResult>.NothingToSave(new SuggestionAcceptanceResult.AmountBreaksRules());
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var problem = await movementChecks.FindProblemAsync(userId, null, bagId, amountInCents, now);

        if (problem != null)
        {
            return PreparedChanges<SuggestionAcceptanceResult>.NothingToSave(Translate(problem));
        }

        var movement = new BagMovement
        {
            AmountInCents = amountInCents,
            MovedAt = now,
            DestinationBagId = bagId,
            SavingsSuggestionId = suggestion.Id
        };

        bagRepository.PrepareMovement(movement);
        await goalAlerts.PrepareAfterArrivalAsync(userId, bagId, amountInCents, now);
        suggestion.Accept(now);
        bagRepository.PrepareUserCheck(user);

        return PreparedChanges<SuggestionAcceptanceResult>.SaveThenReturn(new SuggestionAcceptanceResult.Accepted { Movement = movement });
    }

    // Donne le refus d'un déplacement depuis l'argent disponible sous la forme d'une acceptation. Un sac de départ trop vide
    // ne peut pas arriver : l'argent part de l'argent disponible.
    private static SuggestionAcceptanceResult Translate(BagMovementResult problem)
    {
        if (problem is BagMovementResult.NotFound)
        {
            return new SuggestionAcceptanceResult.BagNotFound();
        }

        if (problem is BagMovementResult.TooManyMovements)
        {
            return new SuggestionAcceptanceResult.TooManyMovements();
        }

        if (problem is BagMovementResult.AvailableMoneyTooLow)
        {
            return new SuggestionAcceptanceResult.AvailableMoneyTooLow();
        }

        throw new InvalidOperationException("Un déplacement depuis l'argent disponible ne peut pas être refusé ainsi.");
    }
}
