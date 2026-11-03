namespace WalletFlow.Domain.AccountsAndCategories;

// Opération reçue de la banque.
// Correspond à la table transaction du MPD (DD §5.3).
public class BankTransaction
{
    // id_transaction
    public Guid Id { get; set; } = Guid.NewGuid();

    // id_transaction_externe : identifiant de l'opération chez le prestataire.
    public string ExternalTransactionId { get; set; } = string.Empty;

    // date_transaction
    public DateOnly TransactionDate { get; set; }

    // libelle_transaction : contenu non fiable, toujours affiché comme du texte.
    public string Label { get; set; } = string.Empty;

    // montant_transaction : en centimes, négatif pour une dépense, positif pour un revenu.
    public int AmountInCents { get; set; }

    // est_transfert_interne : un transfert n'est compté ni en dépense ni en revenu.
    public bool IsInternalTransfer { get; set; } = false;

    // id_compte
    public Guid BankAccountId { get; set; }

    // id_categorie
    public Guid CategoryId { get; set; }
}
