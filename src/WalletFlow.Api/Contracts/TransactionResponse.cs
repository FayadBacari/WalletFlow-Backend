namespace WalletFlow.Api.Contracts;

// Une opération bancaire, telle que l'utilisateur connecté la voit (GR Comptes). Le montant est en centimes, négatif
// pour une dépense. Le libellé vient de la banque : le site l'affiche toujours comme du texte, jamais comme du HTML (SS §5).
public record TransactionResponse(
    Guid Id,
    DateOnly Date,
    string Label,
    int AmountInCents,
    bool IsInternalTransfer,
    Guid AccountId,
    string AccountName,
    Guid CategoryId,
    string CategoryName);

// Une page de résultats : les opérations, le numéro de la page, sa taille, et le nombre de toutes les opérations trouvées.
public record TransactionPageResponse(List<TransactionResponse> Items, int Page, int PageSize, int TotalCount);

// Le solde total de l'utilisateur, tous comptes confondus, en centimes (GR Comptes), la date du plus ancien des soldes
// additionnés, null sans aucun compte, et le nombre de comptes.
public record BalanceTotalResponse(long TotalInCents, DateTime? OldestBalanceUpdatedAt, int AccountCount);
