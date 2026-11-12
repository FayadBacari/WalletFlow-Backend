namespace WalletFlow.Application.Transactions;

// Résultat du marquage d'une opération comme transfert : toujours une seule des formes écrites dans ce fichier.
public abstract class TransactionTransferResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private TransactionTransferResult()
    {
    }

    // L'opération porte la marque demandée.
    public sealed class Marked : TransactionTransferResult
    {
        public required TransactionView Transaction { get; init; }
    }

    // L'opération n'existe pas, ou elle est à un autre utilisateur.
    public sealed class NotFound : TransactionTransferResult
    {
    }

    // D'autres requêtes changent l'opération sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : TransactionTransferResult
    {
    }
}
