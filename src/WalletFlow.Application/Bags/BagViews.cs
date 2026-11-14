using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Un sac tel que l'utilisateur le voit : son solde en centimes (DD §11) et sa progression vers son objectif, null sans objectif.
public record BagView(
    Guid Id,
    string Name,
    long BalanceInCents,
    int? TargetAmountInCents,
    DateOnly? TargetDate,
    int? ProgressPercent)
{
    // Donne la vue d'un sac qui a ce solde.
    public static BagView Create(Bag bag, long balanceInCents)
    {
        return new BagView(
            bag.Id,
            bag.Name,
            balanceInCents,
            bag.TargetAmountInCents,
            bag.TargetDate,
            BagRules.CountProgressPercent(balanceInCents, bag.TargetAmountInCents));
    }
}

// Les sacs de l'utilisateur, avec son solde total, le total de ses sacs et son argent disponible, en centimes (DD §11).
// L'argent disponible est négatif quand les sacs dépassent le solde total : c'est le montant à retirer des sacs (GR Sacs).
// La date du plus ancien solde dit de quand date le solde total, null sans aucun compte.
public record BagSummary(
    List<BagView> Bags,
    long TotalBalanceInCents,
    long BagsTotalInCents,
    long AvailableInCents,
    DateTime? OldestBalanceUpdatedAt);

// Un mouvement vu depuis un sac : le montant est positif quand l'argent arrive dans le sac, négatif quand il en part.
// L'autre côté est un autre sac de l'utilisateur, ou l'argent disponible quand OtherBagId est null. Quand l'autre sac est supprimé,
// le mouvement compte comme un mouvement avec l'argent disponible (MPD) : le solde du sac reste juste.
public record BagHistoryItem(Guid Id, DateTime MovedAt, int AmountInCents, Guid? OtherBagId, string? OtherBagName);

// Une page de l'historique d'un sac, et le nombre de tous ses mouvements.
public record BagHistoryPage(List<BagHistoryItem> Items, int TotalCount);
