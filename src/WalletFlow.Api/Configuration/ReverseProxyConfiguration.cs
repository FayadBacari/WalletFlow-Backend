using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace WalletFlow.Api.Configuration;

// Derrière un proxy inverse, l'adresse du client et le protocole d'origine arrivent dans les en-têtes X-Forwarded-For et
// X-Forwarded-Proto (SS §6). Ils ne sont crus que si la requête vient d'un proxy déclaré : sinon, n'importe qui choisirait
// l'adresse sous laquelle il est compté par les limites et noté au journal. Les proxys se déclarent dans la section
// ReverseProxy : KnownProxies (des adresses) et KnownNetworks (des réseaux, par exemple 10.0.0.0/24). Sans déclaration, seul
// un proxy sur la même machine (127.0.0.0/8 ou ::1) est cru, comme le prévoit ASP.NET Core. Une valeur illisible empêche
// l'API de démarrer : un proxy mal déclaré compterait tous les visiteurs sous une seule adresse.
public sealed class ReverseProxyConfiguration
{
    public const string SectionName = "ReverseProxy";
    public const string KnownProxiesSetting = SectionName + ":KnownProxies";
    public const string KnownNetworksSetting = SectionName + ":KnownNetworks";

    // Garde les proxys déclarés, déjà lus et vérifiés par Read : il n'y a pas d'autre façon de créer ces réglages.
    private ReverseProxyConfiguration(List<IPAddress> knownProxies, List<System.Net.IPNetwork> knownNetworks)
    {
        KnownProxies = knownProxies;
        KnownNetworks = knownNetworks;
    }

    public IReadOnlyList<IPAddress> KnownProxies { get; }
    public IReadOnlyList<System.Net.IPNetwork> KnownNetworks { get; }

    // Lit les proxys déclarés. Refuse une adresse ou un réseau illisible.
    public static ReverseProxyConfiguration Read(IConfiguration configuration)
    {
        var knownProxies = new List<IPAddress>();
        foreach (var text in ReadList(configuration, KnownProxiesSetting))
        {
            if (!IPAddress.TryParse(text, out var address))
            {
                throw new InvalidOperationException("« " + text + " » n'est pas une adresse IP, dans " + KnownProxiesSetting + ".");
            }

            knownProxies.Add(address);
        }

        var knownNetworks = new List<System.Net.IPNetwork>();
        foreach (var text in ReadList(configuration, KnownNetworksSetting))
        {
            if (!System.Net.IPNetwork.TryParse(text, out var network))
            {
                throw new InvalidOperationException("« " + text + " » n'est pas un réseau comme 10.0.0.0/24, dans " + KnownNetworksSetting + ".");
            }

            knownNetworks.Add(network);
        }

        return new ReverseProxyConfiguration(knownProxies, knownNetworks);
    }

    // Règle la lecture des en-têtes du proxy : l'adresse du client et le protocole, depuis le dernier proxy seulement.
    public void Apply(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;

        foreach (var proxy in KnownProxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (var network in KnownNetworks)
        {
            options.KnownIPNetworks.Add(network);
        }
    }

    // Lit une liste de la configuration, sans les valeurs vides.
    private static IEnumerable<string> ReadList(IConfiguration configuration, string settingName)
    {
        return configuration.GetSection(settingName).GetChildren()
            .Select(child => child.Value?.Trim() ?? "")
            .Where(text => text != "");
    }
}
