namespace WalletFlow.Application.BankConnections;

// Une opération donnée par la banque, avant tout contrôle : rien n'est cru sans être vérifié (SS §5).
// Reference : la référence de l'opération chez la banque, quand elle en donne une. IsBooked : la banque l'a comptabilisée.
// Date : la date comptable, ou à défaut la date de valeur ; null si la banque n'en donne aucune lisible.
// AmountInCents : négatif pour un débit, positif pour un crédit ; null s'il est illisible. Label : le libellé tel que reçu.
public record ProvidedTransaction(string? Reference, bool IsBooked, DateOnly? Date, long? AmountInCents, string Currency, string Label);
