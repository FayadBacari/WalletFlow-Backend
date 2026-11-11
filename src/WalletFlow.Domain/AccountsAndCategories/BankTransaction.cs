namespace WalletFlow.Domain.AccountsAndCategories;

// Opération reçue de la banque.
// Correspond à la table transaction du MPD (DD §5.3).
public class BankTransaction
{
    // id_transaction
    public Guid Id { get; init; } = Guid.NewGuid();

    // id_transaction_externe : identifiant de l'opération chez le prestataire.
    public string ExternalTransactionId { get; init; } = string.Empty;

    // date_transaction
    public DateOnly TransactionDate { get; init; }

    // libelle_transaction : contenu non fiable, toujours affiché comme du texte.
    public string Label { get; init; } = string.Empty;

    // montant_transaction : en centimes, négatif pour une dépense, positif pour un revenu.
    public int AmountInCents { get; init; }

    // est_transfert_interne : un transfert n'est compté ni en dépense ni en revenu.
    public bool IsInternalTransfer { get; init; } = false;

    // id_compte
    public Guid BankAccountId { get; init; }

    // id_categorie
    public Guid CategoryId { get; init; }
}
