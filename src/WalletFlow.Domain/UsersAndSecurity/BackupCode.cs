namespace WalletFlow.Domain.UsersAndSecurity;

// Code de secours à usage unique, remis à l'activation de la double authentification.
// Correspond à la table code_secours du MPD (DD §4.1). Les dates sont en UTC.
// Chaque valeur se donne à la création. Ensuite, le code ne peut que servir, une seule fois.
public class BackupCode
{
    private DateTime? usedAt = null;

    // id_code_secours
    public Guid Id { get; init; } = Guid.NewGuid();

    // empreinte_code_secours : le code est haché comme un mot de passe, jamais stocké en clair.
    public string CodeHash { get; init; } = string.Empty;

    // date_utilisation_code_secours : vide tant que le code n'a pas servi.
    public DateTime? UsedAt
    {
        get => usedAt;
        init => usedAt = value;
    }

    // id_utilisateur
    public Guid UserId { get; init; }

    // Note que le code vient de servir. Un code de secours ne sert qu'une fois (GR Auth).
    public void MarkAsUsed(DateTime now)
    {
        if (usedAt != null)
        {
            throw new InvalidOperationException("Ce code de secours a déjà servi : il ne peut plus servir.");
        }

        usedAt = now;
    }
}
