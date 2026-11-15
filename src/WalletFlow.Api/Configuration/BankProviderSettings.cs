namespace WalletFlow.Api.Configuration;

// Les réglages de l'application Enable Banking, lus dans les secrets (user-secrets en développement,
// variables d'environnement en production), jamais dans le code ni dans Git (SS §7).
public record BankProviderSettings(string ApplicationId, string PrivateKeyPath)
{
    public const string ApplicationIdSetting = "EnableBanking:ApplicationId";
    public const string PrivateKeyPathSetting = "EnableBanking:PrivateKeyPath";

    // Lit les deux réglages. Renvoie null s'il en manque un : la connexion bancaire n'est alors pas configurée.
    public static BankProviderSettings? Read(IConfiguration configuration)
    {
        var applicationId = configuration[ApplicationIdSetting];
        var privateKeyPath = configuration[PrivateKeyPathSetting];

        if (string.IsNullOrWhiteSpace(applicationId) || string.IsNullOrWhiteSpace(privateKeyPath))
        {
            return null;
        }

        return new BankProviderSettings(applicationId, privateKeyPath);
    }
}
