namespace WalletFlow.Domain.AccountsAndCategories;

// Connexion à une banque, en lecture seule, par l'intermédiaire du prestataire agréé.
// Correspond à la table connexion_bancaire du MPD (DD §5.1). Les dates sont en UTC.
public class BankConnection
{
    private string externalConnectionId = string.Empty;
    private BankConnectionStatus status = BankConnectionStatus.Active;
    private DateTime authorizationExpiresAt;

    // id_connexion
    public Guid Id { get; init; } = Guid.NewGuid();

    // id_connexion_externe : identifiant de la connexion chez le prestataire. Il change à chaque renouvellement.
    public string ExternalConnectionId
    {
        get => externalConnectionId;
        init => externalConnectionId = value;
    }

    // nom_banque : fourni par le prestataire.
    public string BankName { get; init; } = string.Empty;

    // statut_connexion : active par défaut.
    public BankConnectionStatus Status
    {
        get => status;
        init => status = value;
    }

    // date_expiration_autorisation : au plus 180 jours après l'autorisation donnée à la banque (DD §5.1).
    public DateTime AuthorizationExpiresAt
    {
        get => authorizationExpiresAt;
        init => authorizationExpiresAt = value;
    }

    // id_utilisateur
    public Guid UserId { get; init; }

    // Renvoie vrai si l'autorisation donnée à la banque est finie à cette date.
    public bool HasAuthorizationEnded(DateTime now)
    {
        return now >= authorizationExpiresAt;
    }

    // Renvoie vrai si l'utilisateur doit être prévenu : l'autorisation finit dans 7 jours ou moins (GR Comptes).
    public bool IsEndingSoon(DateTime now)
    {
        return status != BankConnectionStatus.Expired
            && !HasAuthorizationEnded(now)
            && authorizationExpiresAt - now <= BankConnectionRules.RenewalWarningDelay;
    }

    // La banque a répondu : une connexion en erreur redevient active. Une connexion expirée ne revient que par un renouvellement.
    public void RecordSuccessfulUpdate()
    {
        if (status == BankConnectionStatus.InError)
        {
            status = BankConnectionStatus.Active;
        }
    }

    // La banque ne répond pas : la connexion passe en erreur, et le dernier solde reste affiché avec sa date (GR Comptes).
    public void RecordBankFailure()
    {
        if (status == BankConnectionStatus.Active)
        {
            status = BankConnectionStatus.InError;
        }
    }

    // L'autorisation est finie, ou la banque l'a retirée : elle est à renouveler (GR Comptes).
    public void MarkExpired()
    {
        status = BankConnectionStatus.Expired;
    }

    // Une nouvelle autorisation remplace l'ancienne : nouvel identifiant chez le prestataire, nouvelle date de fin.
    // Les comptes de la connexion restent, avec leurs transactions.
    public void Renew(string newExternalConnectionId, DateTime newAuthorizationEnd)
    {
        if (string.IsNullOrWhiteSpace(newExternalConnectionId)
            || newExternalConnectionId.Length > BankConnectionRules.MaximumExternalIdLength)
        {
            throw new ArgumentException("L'identifiant du prestataire est vide ou trop long pour la base.", nameof(newExternalConnectionId));
        }

        externalConnectionId = newExternalConnectionId;
        authorizationExpiresAt = newAuthorizationEnd;
        status = BankConnectionStatus.Active;
    }
}
