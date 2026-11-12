namespace WalletFlow.Domain.AccountsAndCategories;

// Compte bancaire reçu de la banque.
// Correspond à la table compte_bancaire du MPD (DD §5.2). Les dates sont en UTC.
public class BankAccount
{
    private string name = string.Empty;
    private int balanceInCents;
    private DateTime balanceUpdatedAt;

    // id_compte
    public Guid Id { get; init; } = Guid.NewGuid();

    // id_compte_externe : identifiant stable du compte chez le prestataire, le même d'une autorisation à l'autre.
    public string ExternalAccountId { get; init; } = string.Empty;

    // nom_compte : fourni par la banque, 50 caractères au plus.
    public string Name
    {
        get => name;
        init => name = value;
    }

    // solde_compte : en centimes, peut être négatif.
    public int BalanceInCents
    {
        get => balanceInCents;
        init => balanceInCents = value;
    }

    // date_solde : date et heure de la dernière mise à jour réussie.
    public DateTime BalanceUpdatedAt
    {
        get => balanceUpdatedAt;
        init => balanceUpdatedAt = value;
    }

    // id_connexion
    public Guid BankConnectionId { get; init; }

    // Remplace le solde par celui que la banque vient de donner, avec la date de cette mise à jour réussie (DD §5.2).
    public void UpdateBalance(int newBalanceInCents, DateTime updatedAt)
    {
        if (!BankConnectionRules.IsBalanceValid(newBalanceInCents))
        {
            throw new ArgumentOutOfRangeException(nameof(newBalanceInCents), "Un solde va de -1 000 000 000 à 1 000 000 000 centimes.");
        }

        balanceInCents = newBalanceInCents;
        balanceUpdatedAt = updatedAt;
    }

    // Prend le nom que la banque donne maintenant. Il arrive déjà sous sa forme enregistrée (voir BankConnectionRules).
    public void Rename(string newName)
    {
        var expectedName = BankConnectionRules.CleanReceivedName(
            newName,
            BankConnectionRules.MaximumAccountNameLength,
            BankConnectionRules.AccountNameReplacement);

        if (newName != expectedName)
        {
            throw new ArgumentException("Le nom du compte n'a pas sa forme enregistrée.", nameof(newName));
        }

        name = newName;
    }
}
