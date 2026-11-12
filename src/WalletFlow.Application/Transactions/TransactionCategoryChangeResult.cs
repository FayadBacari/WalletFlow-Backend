namespace WalletFlow.Application.Transactions;

// Résultat du changement de catégorie d'une opération : toujours une seule des formes écrites dans ce fichier.
public abstract class TransactionCategoryChangeResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private TransactionCategoryChangeResult()
    {
    }

    // L'opération est dans sa nouvelle catégorie, et la règle est enregistrée si l'utilisateur l'a demandée.
    public sealed class Changed : TransactionCategoryChangeResult
    {
        public required TransactionView Transaction { get; init; }
    }

    // L'opération n'existe pas, ou elle est à un autre utilisateur.
    public sealed class TransactionNotFound : TransactionCategoryChangeResult
    {
    }

    // La catégorie n'existe pas, ou elle est à un autre utilisateur.
    public sealed class CategoryNotFound : TransactionCategoryChangeResult
    {
    }

    // L'utilisateur demande une règle, mais le libellé n'a aucune lettre : aucune règle ne pourrait le reconnaître.
    // Rien n'est changé.
    public sealed class LabelWithoutLetter : TransactionCategoryChangeResult
    {
    }

    // D'autres requêtes changent les opérations ou les catégories sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : TransactionCategoryChangeResult
    {
    }
}
