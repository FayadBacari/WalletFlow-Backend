using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Cas d'utilisation : l'utilisateur connecté déplace de l'argent disponible vers un sac, d'un sac à un autre,
// ou d'un sac vers l'argent disponible (GR Sacs). Un déplacement ne rend jamais un sac négatif, et ne porte jamais
// le total des sacs au-dessus du solde total. Le serveur vérifie ces règles, un déplacement à la fois (SS §5).
public class BagMovementService
{
    private readonly IBagRepository bagRepository;
    private readonly BankBalanceTotalService balanceTotalService;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux sacs, le calcul du solde total, l'enregistrement avec réessai, et l'horloge qui date le mouvement.
    public BagMovementService(
        IBagRepository bagRepository,
        BankBalanceTotalService balanceTotalService,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.bagRepository = bagRepository;
        this.balanceTotalService = balanceTotalService;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Déplace ce montant. Un côté null est l'argent disponible : au moins un des deux côtés est un sac,
    // et les deux côtés sont différents (DD §7.2).
    public async Task<BagMovementResult> MoveAsync(Guid userId, Guid? sourceBagId, Guid? destinationBagId, int amountInCents)
    {
        if (sourceBagId == null && destinationBagId == null)
        {
            return new BagMovementResult.NoBag();
        }

        if (sourceBagId == destinationBagId)
        {
            return new BagMovementResult.SameBag();
        }

        if (!BagRules.IsAmountValid(amountInCents))
        {
            return new BagMovementResult.AmountBreaksRules();
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareMovementAsync(userId, sourceBagId, destinationBagId, amountInCents),
            new BagMovementResult.NotSaved());
    }

    // Un passage. La ligne de l'utilisateur est lue d'abord, puis réécrite avec le mouvement : si un autre déplacement
    // de cet utilisateur est enregistré entre-temps, cet enregistrement échoue, et le passage suivant refait les contrôles
    // sur les soldes à jour. Deux déplacements ne peuvent donc pas passer ensemble les contrôles des montants.
    private async Task<PreparedChanges<BagMovementResult>> PrepareMovementAsync(
        Guid userId,
        Guid? sourceBagId,
        Guid? destinationBagId,
        int amountInCents)
    {
        var user = await bagRepository.FindUserAsync(userId);

        if (user == null)
        {
            return PreparedChanges<BagMovementResult>.NothingToSave(new BagMovementResult.NotSaved());
        }

        // SS §4 : les deux sacs doivent être à l'utilisateur. Les soldes lus ne contiennent que ses sacs.
        var balances = await bagRepository.SumBalancesAsync(userId);

        if (sourceBagId is Guid sourceId && !balances.ContainsKey(sourceId))
        {
            return PreparedChanges<BagMovementResult>.NothingToSave(new BagMovementResult.NotFound());
        }

        if (destinationBagId is Guid destinationId && !balances.ContainsKey(destinationId))
        {
            return PreparedChanges<BagMovementResult>.NothingToSave(new BagMovementResult.NotFound());
        }

        var now = clock.GetUtcNow().UtcDateTime;

        if (await bagRepository.CountMovementsSinceAsync(userId, now - BagRules.MovementDay) >= BagRules.MaximumMovementsPerDay)
        {
            return PreparedChanges<BagMovementResult>.NothingToSave(new BagMovementResult.TooManyMovements());
        }

        if (sourceBagId is Guid emptiedBagId && balances[emptiedBagId] < amountInCents)
        {
            return PreparedChanges<BagMovementResult>.NothingToSave(new BagMovementResult.SourceBagTooLow());
        }

        // Seul l'argent qui vient de l'argent disponible fait grandir le total des sacs : lui seul doit tenir sous le solde total.
        // Un déplacement entre deux sacs, ou vers l'argent disponible, reste permis même quand les sacs dépassent déjà ce solde.
        if (sourceBagId == null)
        {
            var total = await balanceTotalService.ReadAsync(userId);
            var bagsTotalInCents = balances.Values.Sum();

            if (bagsTotalInCents + amountInCents > total.TotalInCents)
            {
                return PreparedChanges<BagMovementResult>.NothingToSave(new BagMovementResult.AvailableMoneyTooLow());
            }
        }

        var movement = new BagMovement
        {
            AmountInCents = amountInCents,
            MovedAt = now,
            SourceBagId = sourceBagId,
            DestinationBagId = destinationBagId
        };
        bagRepository.PrepareMovement(movement);
        bagRepository.PrepareUserCheck(user);

        return PreparedChanges<BagMovementResult>.SaveThenReturn(new BagMovementResult.Moved { Movement = movement });
    }
}
