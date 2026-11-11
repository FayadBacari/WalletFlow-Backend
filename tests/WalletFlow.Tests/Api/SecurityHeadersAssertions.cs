namespace WalletFlow.Tests.Api;

// Vérifie les en-têtes de sécurité d'une réponse, avec les valeurs exactes de SS §6.
public static class SecurityHeadersAssertions
{
    // Vérifie que la réponse porte les quatre en-têtes écrits sur chaque réponse de l'API.
    public static void AssertExactSecurityHeaders(HttpResponseMessage response)
    {
        AssertHeaderValue(
            response,
            "Content-Security-Policy",
            "default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'");
        AssertHeaderValue(response, "X-Content-Type-Options", "nosniff");
        AssertHeaderValue(response, "Referrer-Policy", "strict-origin-when-cross-origin");
        AssertHeaderValue(response, "Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    }

    // Vérifie qu'un en-tête est présent une seule fois, avec la valeur attendue.
    public static void AssertHeaderValue(
        HttpResponseMessage response,
        string headerName,
        string value)
    {
        var values = response.Headers.GetValues(headerName);

        Assert.Equal(value, Assert.Single(values));
    }
}
