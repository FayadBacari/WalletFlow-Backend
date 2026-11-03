namespace WalletFlow.Domain.UsersAndSecurity;

// Appareil depuis lequel l'utilisateur s'est déjà connecté.
// Correspond à la table appareil du MPD (DD §4.3).
public class Device
{
    // id_appareil
    public Guid Id { get; set; } = Guid.NewGuid();

    // jeton_appareil : jeton aléatoire et durable, gardé par le navigateur ou le téléphone.
    public string DeviceToken { get; set; } = string.Empty;

    // jeton_push : fourni par l'application mobile si l'utilisateur accepte les notifications.
    public string? PushToken { get; set; }

    // id_utilisateur
    public Guid UserId { get; set; }
}
