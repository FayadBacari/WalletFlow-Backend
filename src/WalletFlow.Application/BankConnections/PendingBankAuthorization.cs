namespace WalletFlow.Application.BankConnections;

// Une autorisation commencée par un utilisateur : la banque choisie, et la connexion qu'elle renouvelle, ou null pour une nouvelle.
public record PendingBankAuthorization(Guid UserId, string BankName, Guid? RenewedConnectionId);
