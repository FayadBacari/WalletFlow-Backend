using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Api.Filters;

namespace WalletFlow.Tests.Api;

// Réglages communs à toute API lancée par les tests, pour qu'elle se comporte de la même façon sur toutes les machines.
// L'environnement « Testing » ne lit ni les user-secrets ni appsettings.Development.json, et les clés Data Protection,
// qui signent le jeton anti-CSRF, restent en mémoire au lieu d'être lues et écrites dans le dossier personnel.
public static class TestApiSettings
{
    public const string EnvironmentName = "Testing";
    public const string WebsiteAddress = "https://walletflow.test";
    public const string AllowedHosts = "localhost;127.0.0.1;walletflow.test";
    public const string BankApplicationId = "application-enable-banking-des-tests";

    // Nom du réglage d'ASP.NET Core qui active ou non la relecture des fichiers appsettings quand ils changent.
    public const string HostReloadSettingName = "hostBuilder:reloadConfigOnChange";

    // Une clé RSA jetable pour la configuration d'Enable Banking : écrite une fois par lancement des tests, effacée à la fin.
    // Les tests remplacent le prestataire : aucune requête n'est jamais signée avec elle.
    private static readonly Lazy<string> ThrowawayBankKeyPath = new Lazy<string>(WriteThrowawayBankKey);

    // Donne à l'API tous ses réglages obligatoires. La base indiquée ici n'est jamais contactée : une fabrique la remplace si besoin.
    public static void Apply(IWebHostBuilder builder, string totpEncryptionKey)
    {
        builder.UseEnvironment(EnvironmentName);

        // Une API surveille ses fichiers appsettings pour les relire quand ils changent. Sous Linux, chaque surveillance compte
        // dans une limite de 128 par utilisateur : au-delà, les API suivantes ne démarrent plus, ce qui a fait échouer la CI
        // de l'étape 10. Les tests ne changent jamais ces fichiers : la surveillance ne leur sert à rien.
        builder.UseSetting(HostReloadSettingName, "false");

        builder.UseSetting("ConnectionStrings:WalletFlowDatabase", "Host=localhost;Database=base_jamais_contactee");
        builder.UseSetting("TwoFactorSecretEncryptionKey", totpEncryptionKey);
        builder.UseSetting("PublicWebsiteAddress", WebsiteAddress);
        // Les noms sous lesquels les tests appellent l'API : le serveur de test, le vrai serveur Kestrel, et le site des tests.
        builder.UseSetting("AllowedHosts", AllowedHosts);
        builder.UseSetting("EnableBanking:ApplicationId", BankApplicationId);
        builder.UseSetting("EnableBanking:PrivateKeyPath", ThrowawayBankKeyPath.Value);

        // Le dossier n'est jamais utilisé : les tests remplacent l'envoi des e-mails.
        builder.UseSetting("LocalEmailsFolder", "dossier-jamais-utilise");

        // Les tests n'attendent pas la durée minimale de réponse : elle a ses propres tests.
        builder.UseSetting(MinimumAnswerDurationFilter.SettingName, "0");

        builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // La mise à jour des soldes en arrière-plan ne tourne pas pendant les tests : sur la base de test,
            // elle toucherait les connexions de la base locale. Ses tests la créent eux-mêmes.
            var bankBackgroundTask = services.FirstOrDefault(service => service.ImplementationType == typeof(BankBalancesBackgroundService));
            if (bankBackgroundTask != null)
            {
                services.Remove(bankBackgroundTask);
            }
        });
    }

    // Écrit la clé jetable dans le dossier temporaire, et la fait effacer quand les tests se terminent.
    private static string WriteThrowawayBankKey()
    {
        using var key = RSA.Create(2048);
        var path = Path.Combine(Path.GetTempPath(), "walletflow-tests-cle-jetable-" + Guid.NewGuid().ToString("N") + ".pem");
        File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
        AppDomain.CurrentDomain.ProcessExit += (sender, arguments) => File.Delete(path);

        return path;
    }
}
