using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Filters;

namespace WalletFlow.Tests.Api;

// Réglages communs à toute API lancée par les tests, pour qu'elle se comporte de la même façon sur toutes les machines.
// L'environnement « Testing » ne lit ni les user-secrets ni appsettings.Development.json, et les clés Data Protection,
// qui signent le jeton anti-CSRF, restent en mémoire au lieu d'être lues et écrites dans le dossier personnel.
public static class TestApiSettings
{
    public const string EnvironmentName = "Testing";
    public const string WebsiteAddress = "https://walletflow.test";

    // Donne à l'API tous ses réglages obligatoires. La base indiquée ici n'est jamais contactée : une fabrique la remplace si besoin.
    public static void Apply(IWebHostBuilder builder, string totpEncryptionKey)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:WalletFlowDatabase", "Host=localhost;Database=base_jamais_contactee");
        builder.UseSetting("TwoFactorSecretEncryptionKey", totpEncryptionKey);
        builder.UseSetting("PublicWebsiteAddress", WebsiteAddress);

        // Le dossier n'est jamais utilisé : les tests remplacent l'envoi des e-mails.
        builder.UseSetting("LocalEmailsFolder", "dossier-jamais-utilise");

        // Les tests n'attendent pas la durée minimale de réponse : elle a ses propres tests.
        builder.UseSetting(MinimumAnswerDurationFilter.SettingName, "0");

        builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    }
}
