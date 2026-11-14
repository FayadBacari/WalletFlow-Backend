using System.Globalization;
using WalletFlow.Application.BankConnections;

namespace WalletFlow.Application.Bags;

// Cas d'utilisation : l'utilisateur connecté voit ses sacs, leur solde, leur progression, et son argent disponible (GR Sacs).
// Rien de tout cela n'est enregistré : tout est recalculé à chaque fois (DD §11).
public class BagListService
{
    // L'ordre alphabétique français : « Épargne » se range avec les E, pas après le Z.
    private static readonly CompareInfo FrenchOrder = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

    private readonly IBagRepository bagRepository;
    private readonly BankBalanceTotalService balanceTotalService;

    // Reçoit l'accès aux sacs et le calcul du solde total.
    public BagListService(IBagRepository bagRepository, BankBalanceTotalService balanceTotalService)
    {
        this.bagRepository = bagRepository;
        this.balanceTotalService = balanceTotalService;
    }

    // Donne les sacs dans l'ordre alphabétique. Le solde total est celui de tous les comptes, le même que GET api/balance.
    public async Task<BagSummary> ListAsync(Guid userId)
    {
        var bags = await bagRepository.FindAllAsync(userId);
        var balances = await bagRepository.SumBalancesAsync(userId);
        var total = await balanceTotalService.ReadAsync(userId);
        var views = new List<BagView>();
        long bagsTotalInCents = 0;

        foreach (var bag in bags)
        {
            var balanceInCents = balances.GetValueOrDefault(bag.Id);
            views.Add(BagView.Create(bag, balanceInCents));
            bagsTotalInCents = bagsTotalInCents + balanceInCents;
        }

        views.Sort(Compare);

        return new BagSummary(
            views,
            total.TotalInCents,
            bagsTotalInCents,
            total.TotalInCents - bagsTotalInCents,
            total.OldestBalanceUpdatedAt);
    }

    // Range les sacs par nom. Deux sacs peuvent porter le même nom : ils sont alors rangés par identifiant,
    // pour garder le même ordre d'un affichage à l'autre.
    private static int Compare(BagView first, BagView second)
    {
        var result = FrenchOrder.Compare(first.Name, second.Name);

        if (result != 0)
        {
            return result;
        }

        return first.Id.CompareTo(second.Id);
    }
}
