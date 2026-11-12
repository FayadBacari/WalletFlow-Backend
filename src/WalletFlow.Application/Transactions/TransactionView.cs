namespace WalletFlow.Application.Transactions;

// Une opération telle que l'utilisateur la voit : avec le nom de son compte et celui de sa catégorie.
public record TransactionView(
    Guid Id,
    DateOnly Date,
    string Label,
    int AmountInCents,
    bool IsInternalTransfer,
    Guid AccountId,
    string AccountName,
    Guid CategoryId,
    string CategoryName);

// Ce que l'utilisateur cherche (GR Comptes) : chaque critère absent ne filtre rien. Text est cherché dans le libellé,
// sans tenir compte des majuscules ; AmountInCents est cherché sans son signe ; From et To sont compris.
public record TransactionSearch(
    string? Text,
    int? AmountInCents,
    DateOnly? From,
    DateOnly? To,
    Guid? AccountId,
    Guid? CategoryId,
    int Page,
    int PageSize);

// Une page de résultats, et le nombre de toutes les opérations trouvées.
public record TransactionPage(List<TransactionView> Items, int TotalCount);
