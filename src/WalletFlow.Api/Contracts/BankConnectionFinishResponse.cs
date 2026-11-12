namespace WalletFlow.Api.Contracts;

// Réponse au retour de la banque : la connexion, s'il s'agit d'un renouvellement, le nombre de comptes ajoutés,
// et les noms des comptes laissés de côté (autre devise que l'euro, solde illisible, ou déjà connecté ailleurs).
public record BankConnectionFinishResponse(Guid ConnectionId, bool IsRenewal, int AddedAccountCount, List<string> SkippedAccountNames);
