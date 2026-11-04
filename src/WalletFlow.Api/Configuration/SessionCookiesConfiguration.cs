namespace WalletFlow.Api.Configuration;

// Noms et réglages des cookies de connexion (SS §3). Le préfixe __Host- oblige le navigateur à refuser
// un cookie sans Secure, avec un domaine, ou sur un autre chemin que / : un sous-domaine ne peut donc pas l'imposer.
public static class SessionCookiesConfiguration
{
    public const string NameOfTheUserSessionCookie = "__Host-walletflow-session";
    public const string NameOfTheDeviceTokenCookie = "__Host-walletflow-device";

    public static readonly TimeSpan DurationOfTheDeviceTokenCookie = TimeSpan.FromDays(365);

    // Cookie de session : sans date de fin, il disparaît à la fermeture du navigateur.
    // Les 30 minutes d'inactivité sont contrôlées par le serveur.
    public static CookieOptions CreateOptionsOfTheUserSessionCookie()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        };
    }

    // Cookie d'appareil : durable, pour reconnaître le navigateur à la prochaine connexion (SS §3).
    public static CookieOptions CreateOptionsOfTheDeviceTokenCookie()
    {
        var optionsOfTheDeviceTokenCookie = CreateOptionsOfTheUserSessionCookie();
        optionsOfTheDeviceTokenCookie.MaxAge = DurationOfTheDeviceTokenCookie;

        return optionsOfTheDeviceTokenCookie;
    }
}
