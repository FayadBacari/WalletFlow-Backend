namespace WalletFlow.Api.Contracts;

// Critères d'un graphique sur une période, lus dans l'adresse : la date de début et la date de fin, comprises,
// écrites comme 2026-11-12, et un compte facultatif. Sans compte, tous les comptes de l'utilisateur comptent (GR Dépenses).
public class ChartPeriodRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? AccountId { get; set; }
}

// Critère de la comparaison du mois en cours, lu dans l'adresse : un compte facultatif.
public class ChartAccountRequest
{
    public Guid? AccountId { get; set; }
}
