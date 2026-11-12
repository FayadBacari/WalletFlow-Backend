namespace WalletFlow.Application.BankConnections;

// Ce qu'il faut au prestataire pour commencer une autorisation : la banque, la fin de l'autorisation,
// le paramètre state qui reviendra avec l'utilisateur, et l'adresse fixe du site où la banque le renverra (SS §7).
public record BankAuthorizationRequest(string BankName, DateTime ValidUntil, string State, string ReturnAddress);
