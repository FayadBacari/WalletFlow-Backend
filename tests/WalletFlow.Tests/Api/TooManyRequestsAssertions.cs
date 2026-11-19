using System.Globalization;
using System.Net;
using System.Text.Json;

namespace WalletFlow.Tests.Api;

// Vérifie le refus « trop de requêtes » (429) tel que le site le reçoit : le délai après lequel réessayer, le lien qui décrit
// le code et le titre en français.
public static class TooManyRequestsAssertions
{
    public const string TooManyRequestsType = "https://tools.ietf.org/html/rfc6585#section-4";

    // Vérifie le refus d'une limite propre à une action : son délai est exactement la durée de sa fenêtre.
    public static async Task AssertTooManyRequestsAsync(HttpResponseMessage response, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, await ReadCheckedDelayAsync(response));
    }

    // Vérifie le refus d'une limite de débit : son délai tombe entre 1 seconde et la durée de la fenêtre de la limite.
    public static async Task AssertTooManyRequestsWithinAsync(HttpResponseMessage response, TimeSpan window)
    {
        Assert.InRange(await ReadCheckedDelayAsync(response), 1, (int)window.TotalSeconds);
    }

    // Vérifie le code, le corps et l'en-tête du refus, puis donne son délai en secondes.
    private static async Task<int> ReadCheckedDelayAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(TooManyRequestsType, body.GetProperty("type").GetString());
        Assert.Equal("Trop de demandes. Réessaie plus tard.", body.GetProperty("title").GetString());
        Assert.Equal(429, body.GetProperty("status").GetInt32());

        var retryAfter = Assert.Single(response.Headers.GetValues("Retry-After"));
        Assert.Matches("^[0-9]+$", retryAfter);

        return int.Parse(retryAfter, CultureInfo.InvariantCulture);
    }
}
