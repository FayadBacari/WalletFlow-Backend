using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using WalletFlow.Application.Security;

namespace WalletFlow.Infrastructure.Security;

// Cherche un mot de passe dans Pwned Passwords, la liste publique des mots de passe des fuites connues (SS §2). Le mot de
// passe ne quitte jamais le serveur : seuls les 5 premiers caractères de son empreinte SHA-1 partent, et le service renvoie
// toutes les fins d'empreinte qui commencent ainsi (k-anonymat). L'en-tête Add-Padding lui fait ajouter de fausses lignes,
// comptées 0 fois : la taille de la réponse ne trahit pas le début de l'empreinte. Si le service ne répond pas, ou pas à
// temps, le mot de passe passe : une panne chez un tiers n'empêche personne de choisir son mot de passe. Elle va au journal
// technique, sans rien du mot de passe.
public class PwnedPasswordsChecker : IBreachedPasswordChecker
{
    public static readonly Uri ApiAddress = new Uri("https://api.pwnedpasswords.com/");
    public static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(3);

    // Une réponse fait environ 40 Ko avec ses fausses lignes : au-delà d'un mégaoctet, elle n'est pas lue.
    public const int MaximumResponseBytes = 1024 * 1024;

    private const int PrefixLength = 5;

    private readonly HttpClient httpClient;
    private readonly ILogger<PwnedPasswordsChecker> logger;

    // Reçoit le client HTTP du service (voir CreateHttpClient) et le journal technique.
    public PwnedPasswordsChecker(HttpClient httpClient, ILogger<PwnedPasswordsChecker> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    // Crée le client HTTP du service : son adresse, une attente courte et une réponse de taille bornée.
    public static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = ApiAddress,
            Timeout = MaximumWait,
            MaxResponseContentBufferSize = MaximumResponseBytes
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WalletFlow");

        return httpClient;
    }

    // Demande les fins d'empreinte qui commencent comme celle du mot de passe, puis cherche la sienne parmi elles.
    public async Task<bool> IsBreachedAsync(string normalizedPassword, CancellationToken cancellationToken = default)
    {
        var hash = ComputeSha1Hash(normalizedPassword);
        var prefix = hash.Substring(0, PrefixLength);
        var suffix = hash.Substring(PrefixLength);

        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "range/" + prefix);
            request.Headers.Add("Add-Padding", "true");
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Pwned Passwords a répondu {Statut} : le mot de passe est accepté sans contrôle des fuites.", (int)response.StatusCode);
                return false;
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Pwned Passwords ne répond pas : le mot de passe est accepté sans contrôle des fuites.");
            return false;
        }
        // Une requête abandonnée par son client n'est pas une panne du service : l'annulation remonte telle quelle.
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Pwned Passwords n'a pas répondu à temps : le mot de passe est accepté sans contrôle des fuites.");
            return false;
        }

        return ContainsSuffix(body, suffix);
    }

    // Calcule l'empreinte SHA-1 du mot de passe, en hexadécimal majuscule, comme le service. SHA-1 ne protège rien ici : il sert
    // seulement d'index dans la liste du service.
    [SuppressMessage("Security", "CA5350", Justification = "Pwned Passwords impose SHA-1, qui sert ici d'index et ne protège rien.")]
    private static string ComputeSha1Hash(string normalizedPassword)
    {
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(normalizedPassword)));
    }

    // Cherche la fin d'empreinte dans les lignes « FIN:NOMBRE » de la réponse. Les fausses lignes comptent 0 fois : elles ne
    // désignent aucun mot de passe divulgué. Une ligne illisible est ignorée.
    private static bool ContainsSuffix(string body, string suffix)
    {
        foreach (var line in body.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var lineSuffix = line.AsSpan(0, separator).Trim();
            var countText = line.AsSpan(separator + 1).Trim();

            if (lineSuffix.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && long.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                && count > 0)
            {
                return true;
            }
        }

        return false;
    }
}
