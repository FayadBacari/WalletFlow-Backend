namespace WalletFlow.Application.BankConnections;

// Résultat de la liste des banques : toujours une seule des formes écrites dans ce fichier.
public abstract class BankListResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BankListResult()
    {
    }

    // Les banques, dans l'ordre alphabétique français.
    public sealed class Listed : BankListResult
    {
        public required List<ProvidedBank> Banks { get; init; }
    }

    // Le prestataire ne répond pas.
    public sealed class ProviderUnavailable : BankListResult
    {
    }
}
