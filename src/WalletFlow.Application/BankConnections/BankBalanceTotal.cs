namespace WalletFlow.Application.BankConnections;

// Le solde total de l'utilisateur, tous comptes confondus, en centimes (DD §11), la date du plus ancien des soldes additionnés
// (null sans aucun compte) et le nombre de comptes.
public record BankBalanceTotal(long TotalInCents, DateTime? OldestBalanceUpdatedAt, int AccountCount);
