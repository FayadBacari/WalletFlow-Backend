using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Cas d'utilisation : l'utilisateur connecté crée un sac (GR Sacs), avec un objectif s'il le souhaite,
// dans la limite de 100 sacs (SS §6). Deux sacs peuvent porter le même nom.
public class BagCreationService
{
    private readonly IBagRepository bagRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux sacs, l'enregistrement avec réessai, et l'horloge qui donne le jour de la saisie.
    public BagCreationService(IBagRepository bagRepository, DatabaseChangesSavingService savingService, TimeProvider clock)
    {
        this.bagRepository = bagRepository;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Crée un sac vide avec le nom tapé et l'objectif donné, s'ils respectent les règles. Sans montant ni date, il n'a pas d'objectif.
    // Le jour de la saisie est celui de Paris, comme partout dans l'application (voir UsersCalendar).
    public async Task<BagCreationResult> CreateAsync(Guid userId, string? typedName, int? targetAmountInCents, DateOnly? targetDate)
    {
        var name = BagRules.NormalizeName(typedName);

        if (!BagRules.IsNameValid(name))
        {
            return new BagCreationResult.NameBreaksRules();
        }

        var today = UsersCalendar.FindToday(clock.GetUtcNow());
        var goalProblem = BagRules.FindGoalProblem(targetAmountInCents, targetDate, today);

        if (goalProblem != BagGoalProblem.None)
        {
            return new BagCreationResult.GoalBreaksRules { Problem = goalProblem };
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareNewBagAsync(userId, name, targetAmountInCents, targetDate, today),
            new BagCreationResult.NotSaved());
    }

    // Un passage : lit d'abord la ligne de l'utilisateur, compte ses sacs, puis prépare le nouveau sac.
    // La ligne de l'utilisateur est réécrite en même temps : de deux créations simultanées, la seconde échoue et recompte.
    // Deux créations ne peuvent donc pas passer ensemble le contrôle du nombre maximum.
    private async Task<PreparedChanges<BagCreationResult>> PrepareNewBagAsync(
        Guid userId,
        string name,
        int? targetAmountInCents,
        DateOnly? targetDate,
        DateOnly today)
    {
        var user = await bagRepository.FindUserAsync(userId);

        if (user == null)
        {
            return PreparedChanges<BagCreationResult>.NothingToSave(new BagCreationResult.NotSaved());
        }

        if (await bagRepository.CountAsync(userId) >= BagRules.MaximumCountPerUser)
        {
            return PreparedChanges<BagCreationResult>.NothingToSave(new BagCreationResult.TooManyBags());
        }

        var newBag = new Bag
        {
            Name = name,
            UserId = userId
        };
        newBag.ReplaceGoal(targetAmountInCents, targetDate, today);
        bagRepository.PrepareBag(newBag);
        bagRepository.PrepareUserCheck(user);

        return PreparedChanges<BagCreationResult>.SaveThenReturn(
            new BagCreationResult.Created { Bag = BagView.Create(newBag, 0) });
    }
}
