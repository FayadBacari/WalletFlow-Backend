namespace WalletFlow.Api.Contracts;

// Un compte bancaire : son nom, son solde en centimes, et la date de la dernière mise à jour réussie (GR Comptes).
public record BankAccountResponse(Guid Id, string Name, int BalanceInCents, DateTime BalanceUpdatedAt);
