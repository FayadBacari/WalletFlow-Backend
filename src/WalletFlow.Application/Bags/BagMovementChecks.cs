using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Les contrôles d'un déplacement d'argent (GR Sacs, SS §4 et §5), communs au déplacement choisi par l'utilisateur
// et à l'acceptation d'une suggestion. L'appelant lit la ligne de l'utilisateur avant, et la réécrit avec le mouvement :
// si un autre déplacement est enregistré entre-temps, l'enregistrement échoue, et le passage suivant refait ces contrôles.
public class BagMovementChecks
{
    private readonly IBagRepository bagRepository;
    private readonly BankBalanceTotalService balanceTotalService;

    // Reçoit l'accès aux sacs et le calcul du solde total.
    public BagMovementChecks(IBagRepository bagRepository, BankBalanceTotalService balanceTotalService)
    {
        this.bagRepository = bagRepository;
        this.balanceTotalService = balanceTotalService;
    }

    // Vérifie le déplacement de ce montant, déjà permis par les règles des montants. Un côté null est l'argent disponible.
    // Renvoie le refus, ou null si le déplacement est permis.
    public async Task<BagMovementResult?> FindProblemAsync(
        Guid userId,
        Guid? sourceBagId,
        Guid? destinationBagId,
        int amountInCents,
        DateTime now)
    {
        // SS §4 : les deux sacs doivent être à l'utilisateur. Les soldes lus ne contiennent que ses sacs.
        var balances = await bagRepository.SumBalancesAsync(userId);

        if (sourceBagId is Guid sourceId && !balances.ContainsKey(sourceId))
        {
            return new BagMovementResult.NotFound();
        }

        if (destinationBagId is Guid destinationId && !balances.ContainsKey(destinationId))
        {
            return new BagMovementResult.NotFound();
        }

        if (await bagRepository.CountMovementsSinceAsync(userId, now - BagRules.MovementDay) >= BagRules.MaximumMovementsPerDay)
        {
            return new BagMovementResult.TooManyMovements();
        }

        if (sourceBagId is Guid emptiedBagId && balances[emptiedBagId] < amountInCents)
        {
            return new BagMovementResult.SourceBagTooLow();
        }

        // Seul l'argent qui vient de l'argent disponible fait grandir le total des sacs : lui seul doit tenir sous le solde total.
        // Un déplacement entre deux sacs, ou vers l'argent disponible, reste permis même quand les sacs dépassent déjà ce solde.
        if (sourceBagId == null)
        {
            var total = await balanceTotalService.ReadAsync(userId);
            var bagsTotalInCents = balances.Values.Sum();

            if (bagsTotalInCents + amountInCents > total.TotalInCents)
            {
                return new BagMovementResult.AvailableMoneyTooLow();
            }
        }

        return null;
    }
}
