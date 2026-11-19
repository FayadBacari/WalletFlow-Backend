namespace WalletFlow.Domain.UsersAndSecurity;

// Appareil depuis lequel l'utilisateur s'est déjà connecté.
// Correspond à la table appareil du MPD (DD §4.3).
public class Device
{
    private DateTime lastUsedAt;

    // id_appareil
    public Guid Id { get; init; } = Guid.NewGuid();

    // jeton_appareil : jeton aléatoire et durable, gardé par le navigateur ou le téléphone.
    public string DeviceToken { get; init; } = string.Empty;

    // jeton_push : fourni par l'application mobile si l'utilisateur accepte les notifications.
    public string? PushToken { get; init; }

    // id_utilisateur
    public Guid UserId { get; init; }

    // date_derniere_connexion_appareil : la dernière connexion depuis cet appareil. Un appareil inutilisé depuis un an est oublié.
    public required DateTime LastUsedAt
    {
        get => lastUsedAt;
        init => lastUsedAt = value;
    }

    // Note une connexion depuis cet appareil. La date ne recule jamais.
    public void RecordUse(DateTime now)
    {
        if (now > lastUsedAt)
        {
            lastUsedAt = now;
        }
    }
}
