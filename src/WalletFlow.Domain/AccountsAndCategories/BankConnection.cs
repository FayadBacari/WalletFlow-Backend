namespace WalletFlow.Domain.AccountsAndCategories;

// Connexion à une banque, en lecture seule, par l'intermédiaire du prestataire agréé.
// Correspond à la table connexion_bancaire du MPD (DD §5.1). Les dates sont en UTC.
public class BankConnection
{
    // id_connexion
    public Guid Id { get; set; } = Guid.NewGuid();

    // id_connexion_externe : identifiant de la connexion chez le prestataire.
    public string ExternalConnectionId { get; set; } = string.Empty;

    // nom_banque : fourni par le prestataire.
    public string BankName { get; set; } = string.Empty;

    // statut_connexion : active par défaut.
    public BankConnectionStatus Status { get; set; } = BankConnectionStatus.Active;

    // date_expiration_autorisation : 180 jours après l'autorisation donnée à la banque.
    public DateTime AuthorizationExpiresAt { get; set; }

    // id_utilisateur
    public Guid UserId { get; set; }
}
