namespace WalletFlow.Domain.UsersAndSecurity;

// Durée et limite des appareils d'un compte (SS §3, DD §4.3 et §12).
public static class DeviceRules
{
    // Le cookie d'appareil dure un an, renouvelé à chaque connexion : un appareil inutilisé depuis un an n'a plus son cookie, et il est
    // oublié, sauf le plus récent du compte, qui garde l'alerte « nouvel appareil » après une longue absence.
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    // Au plus ce nombre d'appareils connus par compte : un nouvel appareil fait oublier le moins récemment utilisé.
    public const int MaximumDevicesPerAccount = 20;
}
