namespace WalletFlow.Application.Transactions;

// Le total net des opérations d'une catégorie sur un mois, en centimes : négatif quand l'argent est surtout sorti.
public record CategoryMonthTotal(Guid CategoryId, int Year, int Month, long NetInCents);
