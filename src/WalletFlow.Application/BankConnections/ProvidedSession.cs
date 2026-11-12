namespace WalletFlow.Application.BankConnections;

// Une session autorisée par la banque : son identifiant chez le prestataire, la fin de l'autorisation, et ses comptes.
public record ProvidedSession(string SessionId, DateTime ValidUntil, List<ProvidedAccount> Accounts);
