using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Cas d'utilisation : l'utilisateur connecté donne un objectif à un de ses sacs, le change ou l'enlève (GR Sacs).
// La date est vérifiée à chaque saisie : elle doit être après le jour de la saisie (DD §7.1), même si elle n'a pas changé.
// Un nouveau montant efface l'alerte de l'ancien, et peut en déclencher une autre, tout de suite si le sac l'atteint déjà.
public class BagGoalService
{
    private readonly IBagRepository bagRepository;
    private readonly BagGoalAlerts goalAlerts;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux sacs, l'alerte d'objectif, l'enregistrement avec réessai, et l'horloge qui donne le jour de la saisie.
    public BagGoalService(
        IBagRepository bagRepository,
        BagGoalAlerts goalAlerts,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.bagRepository = bagRepository;
        this.goalAlerts = goalAlerts;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Fixe l'objectif donné, ou l'enlève avec deux null. Le jour de la saisie est celui de Paris (voir UsersCalendar).
    public async Task<BagGoalResult> ReplaceAsync(Guid userId, Guid bagId, int? targetAmountInCents, DateOnly? targetDate, CancellationToken cancellationToken = default)
    {
        var today = UsersCalendar.FindToday(clock.GetUtcNow());
        var goalProblem = BagRules.FindGoalProblem(targetAmountInCents, targetDate, today);

        if (goalProblem != BagGoalProblem.None)
        {
            return new BagGoalResult.GoalBreaksRules { Problem = goalProblem };
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareGoalAsync(userId, bagId, targetAmountInCents, targetDate, today, cancellationToken),
            new BagGoalResult.NotSaved(),
            cancellationToken);
    }

    // Un passage. La ligne de l'utilisateur est lue d'abord, puis réécrite avec l'objectif, comme pour un déplacement :
    // un déplacement enregistré entre-temps fait refaire le passage, et l'alerte se décide sur le solde à jour.
    // Le solde sert aussi à la progression de la réponse.
    private async Task<PreparedChanges<BagGoalResult>> PrepareGoalAsync(
        Guid userId,
        Guid bagId,
        int? targetAmountInCents,
        DateOnly? targetDate,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var user = await bagRepository.FindUserAsync(userId, cancellationToken);

        if (user == null)
        {
            return PreparedChanges<BagGoalResult>.NothingToSave(new BagGoalResult.NotSaved());
        }

        var bag = await bagRepository.FindAsync(userId, bagId, cancellationToken);

        if (bag == null)
        {
            return PreparedChanges<BagGoalResult>.NothingToSave(new BagGoalResult.NotFound());
        }

        var balances = await bagRepository.SumBalancesAsync(userId, cancellationToken);
        var balanceInCents = balances.GetValueOrDefault(bag.Id);
        var previousTargetInCents = bag.TargetAmountInCents;
        bag.ReplaceGoal(targetAmountInCents, targetDate, today);

        await goalAlerts.PrepareAfterGoalChangeAsync(bag, previousTargetInCents, balanceInCents, clock.GetUtcNow().UtcDateTime, cancellationToken);
        bagRepository.PrepareUserCheck(user);

        return PreparedChanges<BagGoalResult>.SaveThenReturn(
            new BagGoalResult.Changed { Bag = BagView.Create(bag, balanceInCents) });
    }
}
