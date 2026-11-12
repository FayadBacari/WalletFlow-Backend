namespace WalletFlow.Application.Charts;

// Résultat d'un graphique sur une période : toujours une seule des formes écrites dans ce fichier.
public abstract class ChartPeriodResult<TChart>
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private ChartPeriodResult()
    {
    }

    // Les chiffres du graphique.
    public sealed class Found : ChartPeriodResult<TChart>
    {
        public required TChart Chart { get; init; }
    }

    // La date de fin est avant la date de début.
    public sealed class DatesReversed : ChartPeriodResult<TChart>
    {
    }

    // La période dépasse 10 ans (voir ChartRules).
    public sealed class PeriodTooLong : ChartPeriodResult<TChart>
    {
    }
}
