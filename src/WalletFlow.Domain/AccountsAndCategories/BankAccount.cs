namespace WalletFlow.Domain.AccountsAndCategories;

// Compte bancaire reçu de la banque.
// Correspond à la table compte_bancaire du MPD (DD §5.2). Les dates sont en UTC.
public class BankAccount
{
    // id_compte
    public Guid Id { get; init; } = Guid.NewGuid();

    // id_compte_externe : identifiant du compte chez le prestataire.
    public string ExternalAccountId { get; init; } = string.Empty;

    // nom_compte : fourni par la banque, 50 caractères au plus.
    public string Name { get; init; } = string.Empty;

    // solde_compte : en centimes, peut être négatif.
    public int BalanceInCents { get; init; }

    // date_solde : date et heure de la dernière mise à jour réussie.
    public DateTime BalanceUpdatedAt { get; init; }

    // id_connexion
    public Guid BankConnectionId { get; init; }
}
