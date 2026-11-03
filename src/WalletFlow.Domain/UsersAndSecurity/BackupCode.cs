namespace WalletFlow.Domain.UsersAndSecurity;

// Code de secours à usage unique, remis à l'activation de la double authentification.
// Correspond à la table code_secours du MPD (DD §4.1). Les dates sont en UTC.
public class BackupCode
{
    // id_code_secours
    public Guid Id { get; set; } = Guid.NewGuid();

    // empreinte_code_secours : le code est haché comme un mot de passe, jamais stocké en clair.
    public string CodeHash { get; set; } = string.Empty;

    // date_utilisation_code_secours : vide tant que le code n'a pas servi.
    public DateTime? UsedAt { get; set; }

    // id_utilisateur
    public Guid UserId { get; set; }
}
