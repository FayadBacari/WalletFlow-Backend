namespace WalletFlow.Api.Configuration;

// Noms et réglages des cookies de connexion (SS §3). Le préfixe __Host- oblige le navigateur à refuser
// un cookie sans Secure, avec un domaine, ou sur un autre chemin que / : un sous-domaine ne peut donc pas l'imposer.
public static class SessionCookiesConfiguration
{
    public const string SessionCookieName = "__Host-walletflow-session";
    public const string DeviceCookieName = "__Host-walletflow-device";
    public const string WaitingLoginCookieName = "__Host-walletflow-two-factor";

    public static readonly TimeSpan DeviceCookieLifetime = TimeSpan.FromDays(365);

    // Cookie de session : sans date de fin, il disparaît à la fermeture du navigateur.
    // Les 30 minutes d'inactivité sont contrôlées par le serveur.
    public static CookieOptions CreateSessionCookieOptions()
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
    public static CookieOptions CreateDeviceCookieOptions()
    {
        var deviceCookieOptions = CreateSessionCookieOptions();
        deviceCookieOptions.MaxAge = DeviceCookieLifetime;

        return deviceCookieOptions;
    }

    // Cookie de la connexion qui attend son code : il ne sert que pour les requêtes venues du site lui-même.
    // Sans date de fin, comme celui de la session : les 5 minutes d'attente sont contrôlées par le serveur.
    public static CookieOptions CreateWaitingLoginCookieOptions()
    {
        var waitingLoginCookieOptions = CreateSessionCookieOptions();
        waitingLoginCookieOptions.SameSite = SameSiteMode.Strict;

        return waitingLoginCookieOptions;
    }

    // Écrit le cookie de session et le cookie d'appareil, renouvelé à chaque connexion.
    public static void WriteLoginCookies(HttpResponse response, string sessionToken, string deviceToken)
    {
        response.Cookies.Append(SessionCookieName, sessionToken, CreateSessionCookieOptions());
        response.Cookies.Append(DeviceCookieName, deviceToken, CreateDeviceCookieOptions());
    }
}
