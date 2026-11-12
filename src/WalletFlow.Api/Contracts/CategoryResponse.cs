namespace WalletFlow.Api.Contracts;

// Une catégorie, telle que l'utilisateur connecté la voit. Le budget est en centimes, null s'il n'y en a pas.
// IsOtherCategory dit au site que cette catégorie ne se renomme ni ne se supprime.
public record CategoryResponse(Guid Id, string Name, int? MonthlyBudgetInCents, bool IsOtherCategory);
