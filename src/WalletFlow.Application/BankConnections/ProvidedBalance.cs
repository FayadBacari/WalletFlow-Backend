namespace WalletFlow.Application.BankConnections;

// Un solde donné par la banque : son type (code ISO 20022), son montant en centimes, et sa devise (code ISO 4217).
// AmountInCents vaut null si le montant reçu n'est pas un nombre de centimes lisible.
public record ProvidedBalance(string Type, long? AmountInCents, string Currency);
