namespace WalletFlow.Api.Contracts;

// Un sac tel que l'utilisateur connecté le voit (GR Sacs) : son solde et son objectif en centimes, la date de l'objectif,
// et la progression en pourcentage, arrondie vers le bas, null sans objectif. Le nom est du contenu non fiable :
// le site l'affiche toujours comme du texte, jamais comme du HTML (SS §5).
public record BagResponse(
    Guid Id,
    string Name,
    long BalanceInCents,
    int? TargetAmountInCents,
    DateOnly? TargetDate,
    int? ProgressPercent);

// Les sacs dans l'ordre alphabétique, avec le solde total de tous les comptes, le total des sacs et l'argent disponible,
// en centimes (DD §11). L'argent disponible est négatif quand les sacs dépassent le solde total : le site prévient alors
// l'utilisateur et lui demande de retirer ce montant de ses sacs (GR Sacs). La date du plus ancien solde dit de quand date
// le solde total, null sans aucun compte.
public record BagListResponse(
    List<BagResponse> Bags,
    long TotalBalanceInCents,
    long BagsTotalInCents,
    long AvailableInCents,
    DateTime? OldestBalanceUpdatedAt);

// Un déplacement d'argent enregistré, en centimes. Un sac null est l'argent disponible.
public record BagMovementResponse(Guid Id, DateTime MovedAt, int AmountInCents, Guid? SourceBagId, Guid? DestinationBagId);

// Un mouvement de l'historique d'un sac : le montant est positif quand l'argent arrive dans le sac, négatif quand il en part.
// L'autre côté est un autre sac, ou l'argent disponible quand OtherBagId est null.
public record BagHistoryItemResponse(Guid Id, DateTime MovedAt, int AmountInCents, Guid? OtherBagId, string? OtherBagName);

// Une page de l'historique : les mouvements, le numéro de la page, sa taille, et le nombre de tous les mouvements du sac.
public record BagHistoryPageResponse(List<BagHistoryItemResponse> Items, int Page, int PageSize, int TotalCount);
