using Microsoft.AspNetCore.Antiforgery;

namespace WalletFlow.Api.Configuration;

// Réglages du jeton anti-CSRF fourni par ASP.NET Core (SS §6).
public static class AntiforgeryConfiguration
{
    public const string NameOfTheHeaderCarryingTheCsrfToken = "X-CSRF-TOKEN";
    public const string NameOfTheCookieCarryingTheCsrfToken = "__Host-walletflow-csrf";

    // Par défaut, ce cookie n'est pas Secure, et le jeton est aussi lu dans les formulaires :
    // l'API n'accepte que du JSON, le jeton n'est donc lu que dans l'en-tête.
    public static void ApplyOptionsOfTheCsrfToken(AntiforgeryOptions optionsOfTheCsrfTokenToConfigure)
    {
        optionsOfTheCsrfTokenToConfigure.HeaderName = NameOfTheHeaderCarryingTheCsrfToken;
        optionsOfTheCsrfTokenToConfigure.SuppressReadingTokenFromFormBody = true;
        optionsOfTheCsrfTokenToConfigure.SuppressXFrameOptionsHeader = true;
        optionsOfTheCsrfTokenToConfigure.Cookie.Name = NameOfTheCookieCarryingTheCsrfToken;
        optionsOfTheCsrfTokenToConfigure.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    }
}
