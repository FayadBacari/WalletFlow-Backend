namespace WalletFlow.Application.Configuration;

// Adresse publique du site de WalletFlow, qui sert à construire les liens envoyés par e-mail.
// Ces liens pointent uniquement vers le domaine de WalletFlow, en HTTPS (SS §9).
public class PublicWebsiteSettings
{
    public string PublicWebsiteAddress { get; }

    // Refuse une adresse absente ou qui n'est pas en HTTPS.
    public PublicWebsiteSettings(string? websiteAddress)
    {
        if (string.IsNullOrWhiteSpace(websiteAddress))
        {
            throw new InvalidOperationException("Le réglage « PublicWebsiteAddress » est absent.");
        }

        if (!websiteAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Le réglage « PublicWebsiteAddress » doit commencer par https://.");
        }

        PublicWebsiteAddress = websiteAddress.TrimEnd('/');
    }

    // Donne l'adresse fixe d'une page du site, par exemple celle où la banque renvoie l'utilisateur (SS §7).
    public string BuildPageAddress(string pagePath)
    {
        return PublicWebsiteAddress + pagePath;
    }

    // Construit un lien vers une page du site, qui demande une action avant de l'accomplir (SS §2).
    // Le jeton est placé après le # : il n'est jamais envoyé au serveur quand la page s'ouvre,
    // et n'apparaît donc pas dans ses journaux (SS §7).
    public string BuildLink(string pagePath, string token)
    {
        return PublicWebsiteAddress + pagePath + "#token=" + token;
    }
}
