using WalletFlow.Application.Persistence;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Cas d'utilisation : l'utilisateur connecté déplace de l'argent disponible vers un sac, d'un sac à un autre,
// ou d'un sac vers l'argent disponible (GR Sacs). Un déplacement ne rend jamais un sac négatif, et ne porte jamais
// le total des sacs au-dessus du solde total. Le serveur vérifie ces règles, un déplacement à la fois (SS §5).
public class BagMovementService
{
    private readonly IBagRepository bagRepository;
    private readonly BagMovementChecks movementChecks;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux sacs, les contrôles d'un déplacement, l'enregistrement avec réessai, et l'horloge qui date le mouvement.
    public BagMovementService(
        IBagRepository bagRepository,
        BagMovementChecks movementChecks,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.bagRepository = bagRepository;
        this.movementChecks = movementChecks;
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

        var now = clock.GetUtcNow().UtcDateTime;
        var problem = await movementChecks.FindProblemAsync(userId, sourceBagId, destinationBagId, amountInCents, now);

        if (problem != null)
        {
            return PreparedChanges<BagMovementResult>.NothingToSave(problem);
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
