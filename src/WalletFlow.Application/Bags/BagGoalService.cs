using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Cas d'utilisation : l'utilisateur connecté donne un objectif à un de ses sacs, le change ou l'enlève (GR Sacs).
// La date est vérifiée à chaque saisie : elle doit être après le jour de la saisie (DD §7.1), même si elle n'a pas changé.
public class BagGoalService
{
    private readonly IBagRepository bagRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux sacs, l'enregistrement avec réessai, et l'horloge qui donne le jour de la saisie.
    public BagGoalService(IBagRepository bagRepository, DatabaseChangesSavingService savingService, TimeProvider clock)
    {
        this.bagRepository = bagRepository;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Fixe l'objectif donné, ou l'enlève avec deux null. Le jour de la saisie est celui de Paris (voir UsersCalendar).
    public async Task<BagGoalResult> ReplaceAsync(Guid userId, Guid bagId, int? targetAmountInCents, DateOnly? targetDate)
    {
        var today = UsersCalendar.FindToday(clock.GetUtcNow());
        var goalProblem = BagRules.FindGoalProblem(targetAmountInCents, targetDate, today);

        if (goalProblem != BagGoalProblem.None)
        {
            return new BagGoalResult.GoalBreaksRules { Problem = goalProblem };
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareGoalAsync(userId, bagId, targetAmountInCents, targetDate, today),
            new BagGoalResult.NotSaved());
    }

    // Un passage : lit le sac parmi ceux de l'utilisateur, puis prépare le changement. Le solde sert à la progression de la réponse.
    private async Task<PreparedChanges<BagGoalResult>> PrepareGoalAsync(
        Guid userId,
        Guid bagId,
        int? targetAmountInCents,
        DateOnly? targetDate,
        DateOnly today)
    {
        var bag = await bagRepository.FindAsync(userId, bagId);

        if (bag == null)
        {
            return PreparedChanges<BagGoalResult>.NothingToSave(new BagGoalResult.NotFound());
        }

        var balances = await bagRepository.SumBalancesAsync(userId);
        bag.ReplaceGoal(targetAmountInCents, targetDate, today);

        return PreparedChanges<BagGoalResult>.SaveThenReturn(
            new BagGoalResult.Changed { Bag = BagView.Create(bag, balances.GetValueOrDefault(bag.Id)) });
    }
}
