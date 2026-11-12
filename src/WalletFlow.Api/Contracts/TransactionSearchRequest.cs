namespace WalletFlow.Api.Contracts;

// Critères d'une recherche d'opérations, lus dans l'adresse (GR Comptes) : chaque critère absent ne filtre rien.
// Text est cherché dans le libellé, majuscules ignorées. AmountInCents est cherché sans son signe. From et To sont compris,
// écrits comme 2026-11-12. Page commence à 1.
public class TransactionSearchRequest
{
    public string? Text { get; set; }
    public int? AmountInCents { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? CategoryId { get; set; }
    public int? Page { get; set; }
}
