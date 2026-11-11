namespace WalletFlow.Domain.UsersAndSecurity;

// Lien à usage unique envoyé par e-mail : seule l'empreinte du jeton est stockée.
// Correspond à la table jeton du MPD (DD §4.2). Les dates sont en UTC.
// Chaque valeur se donne à la création. Ensuite, le lien ne peut que servir, une seule fois.
public class EmailToken
{
    private DateTime? usedAt = null;

    // id_jeton
    public Guid Id { get; init; } = Guid.NewGuid();

    // type_jeton
    public EmailTokenType Type { get; init; }

    // empreinte_jeton : empreinte SHA-256 du jeton, jamais le jeton lui-même.
    public string TokenHash { get; init; } = string.Empty;

    // date_expiration_jeton
    public DateTime ExpiresAt { get; init; }

    // date_utilisation_jeton : vide tant que le lien n'a pas servi.
    public DateTime? UsedAt
    {
        get => usedAt;
        init => usedAt = value;
    }

    // nouvel_email : seulement pour un changement d'adresse e-mail.
    public string? NewEmail { get; init; }

    // id_utilisateur
    public Guid UserId { get; init; }

    // Règle commune à tous les liens envoyés par e-mail (SS §2) :
    // renvoie vrai si le lien n'a pas encore servi et n'a pas expiré. Le lien expire à sa date de fin exacte.
    public bool CanBeUsedAt(DateTime now)
    {
        return usedAt == null && ExpiresAt > now;
    }

    // Note que le lien vient de servir. Un lien ne sert qu'une seule fois (SS §2).
    public void MarkAsUsed(DateTime now)
    {
        if (usedAt != null)
        {
            throw new InvalidOperationException("Ce lien a déjà servi : il ne peut plus servir.");
        }

        usedAt = now;
    }
}
