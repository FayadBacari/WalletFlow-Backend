namespace WalletFlow.Application.Transactions;

// Une opération reçue de la banque et vérifiée, prête à être ajoutée si elle est nouvelle :
// son identifiant (voir TransactionReception), sa date comptable, son montant en centimes et son libellé enregistré.
public record ReceivedTransaction(string ExternalId, DateOnly Date, int AmountInCents, string Label);
