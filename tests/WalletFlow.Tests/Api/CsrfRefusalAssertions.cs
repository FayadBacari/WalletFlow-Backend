using System.Net;
using System.Text.Json;
using WalletFlow.Api.Configuration;

namespace WalletFlow.Tests.Api;

// Vérifie le refus d'une écriture sans jeton anti-CSRF valable, tel que le site le reçoit : le code, le titre et le lien des
// requêtes refusées, et une seule erreur, sous la clé du jeton, qui dit quoi faire.
public static class CsrfRefusalAssertions
{
    public const string CsrfRefusalMessage = "Le jeton anti-CSRF manque ou n'est pas valide. Demande un nouveau jeton, puis réessaie.";

    // Vérifie que la réponse est ce refus, et rien d'autre.
    public static async Task AssertCsrfRefusalAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.1", body.GetProperty("type").GetString());
        Assert.Equal(ApiErrorConfiguration.RefusedRequestTitle, body.GetProperty("title").GetString());
        Assert.Equal(400, body.GetProperty("status").GetInt32());

        var error = Assert.Single(body.GetProperty("errors").EnumerateObject());
        Assert.Equal("csrfToken", error.Name);
        var message = Assert.Single(error.Value.EnumerateArray());
        Assert.Equal(CsrfRefusalMessage, message.GetString());
    }
}
