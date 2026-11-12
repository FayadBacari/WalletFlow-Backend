namespace WalletFlow.Domain.AccountsAndCategories;

// Opération reçue de la banque.
// Correspond à la table transaction du MPD (DD §5.3). Dans WalletFlow, l'utilisateur peut seulement changer
// sa catégorie, ou la marquer comme transfert entre deux de ses comptes (GR Comptes).
public class BankTransaction
{
    private bool isInternalTransfer = false;
    private Guid categoryId;

    // id_transaction
    public Guid Id { get; init; } = Guid.NewGuid();

    // id_transaction_externe : identifiant de l'opération, tiré de ce que la banque donne (voir TransactionReception).
    public string ExternalTransactionId { get; init; } = string.Empty;

    // date_transaction : la date comptable donnée par la banque.
    public DateOnly TransactionDate { get; init; }

    // libelle_transaction : contenu non fiable, toujours affiché comme du texte.
    public string Label { get; init; } = string.Empty;

    // montant_transaction : en centimes, négatif pour une dépense, positif pour un revenu.
    public int AmountInCents { get; init; }

    // est_transfert_interne : un transfert n'est compté ni en dépense ni en revenu.
    public bool IsInternalTransfer
    {
        get => isInternalTransfer;
        init => isInternalTransfer = value;
    }

    // id_compte
    public Guid BankAccountId { get; init; }

    // id_categorie
    public Guid CategoryId
    {
        get => categoryId;
        init => categoryId = value;
    }

    // Range l'opération dans une autre catégorie (GR Dépenses). Le cas d'utilisation vérifie qu'elle est à l'utilisateur.
    public void ChangeCategory(Guid newCategoryId)
    {
        if (newCategoryId == Guid.Empty)
        {
            throw new ArgumentException("Une opération appartient toujours à une catégorie.", nameof(newCategoryId));
        }

        categoryId = newCategoryId;
    }

    // Marque l'opération comme un transfert entre deux comptes de l'utilisateur, ou retire cette marque (GR Comptes).
    public void MarkAsInternalTransfer(bool isTransfer)
    {
        isInternalTransfer = isTransfer;
    }
}
