using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Filters;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Réglages communs à toute API lancée par les tests, pour qu'elle se comporte de la même façon sur toutes les machines.
// L'environnement « Testing » ne lit ni les user-secrets ni appsettings.Development.json, et les clés Data Protection,
// qui signent le jeton anti-CSRF, sont gardées par le vrai code de production dans un dossier temporaire de ce lancement des
// tests, jamais dans le dossier personnel.
public static class TestApiSettings
{
    public const string EnvironmentName = "Testing";
    public const string WebsiteAddress = "https://walletflow.test";
    public const string AllowedHosts = "localhost;127.0.0.1;walletflow.test";
    public const string BankApplicationId = "application-enable-banking-des-tests";

    // Le nombre de calculs Argon2id à la fois des API de test, le même sur toutes les machines : 8 calculs et 32 en attente,
    // de quoi passer les courses de 20 requêtes simultanées. Réglé sur le nombre de cœurs, il refusait la moitié d'une course
    // sur une machine à 2 cœurs.
    public const int ArgonConcurrentComputations = 8;

    // Nom du réglage d'ASP.NET Core qui active ou non la relecture des fichiers appsettings quand ils changent.
    public const string HostReloadSettingName = "hostBuilder:reloadConfigOnChange";

    // Une clé RSA jetable pour la configuration d'Enable Banking : écrite une fois par lancement des tests, effacée à la fin.
    // Les tests remplacent le prestataire : aucune requête n'est jamais signée avec elle.
    private static readonly Lazy<string> ThrowawayBankKeyPath = new Lazy<string>(WriteThrowawayBankKey);

    // Le dossier des clés Data Protection de ce lancement des tests, commun à toutes ses API, effacé à la fin.
    private static readonly Lazy<string> TestKeysFolder = new Lazy<string>(CreateTestKeysFolder);

    // Le certificat jetable qui chiffre ces clés, et sa clé privée : écrits une fois par lancement des tests, effacés à la fin.
    private static readonly Lazy<(string CertificatePath, string KeyPath)> ThrowawayKeysCertificate =
        new Lazy<(string CertificatePath, string KeyPath)>(() => WriteThrowawayCertificate("cles"));

    // Le dossier des clés Data Protection que reçoivent les API des tests.
    public static string KeysFolder => TestKeysFolder.Value;

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

        // Le dossier sert seulement à démarrer en mode développement : les tests remplacent l'envoi des e-mails.
        builder.UseSetting("LocalEmailsFolder", "dossier-jamais-utilise");

        // Les tests n'attendent pas la durée minimale de réponse : elle a ses propres tests.
        builder.UseSetting(MinimumAnswerDurationFilter.SettingName, "0");
        builder.UseSetting(
            ArgonConfiguration.ConcurrentComputationsSetting,
            ArgonConcurrentComputations.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(DataProtectionConfiguration.KeysFolderSetting, KeysFolder);
        builder.UseSetting(DataProtectionConfiguration.CertificatePathSetting, ThrowawayKeysCertificate.Value.CertificatePath);
        builder.UseSetting(DataProtectionConfiguration.CertificateKeyPathSetting, ThrowawayKeysCertificate.Value.KeyPath);

        builder.ConfigureTestServices(services =>
        {
            // Aucun test n'interroge le vrai Pwned Passwords, ni n'envoie de vrai e-mail : sans réseau, un test ne dépend jamais
            // d'un service extérieur.
            services.AddSingleton<IBreachedPasswordChecker>(new FakeBreachedPasswordChecker());
            services.AddSingleton<IEmailSender>(new FakeEmailSender());

            // La mise à jour des soldes, le calcul des suggestions, les alertes, la suppression des comptes et le rechiffrement
            // des secrets en arrière-plan ne tournent pas pendant les tests : sur la base de test, ils toucheraient les comptes
            // de la base locale, et sur les fausses bases, ils écriraient au milieu d'un test. Leurs tests les créent eux-mêmes.
            RemoveBackgroundTask(services, typeof(BankBalancesBackgroundService));
            RemoveBackgroundTask(services, typeof(SuggestionsBackgroundService));
            RemoveBackgroundTask(services, typeof(AlertsBackgroundService));
            RemoveBackgroundTask(services, typeof(AccountDeletionBackgroundService));
            RemoveBackgroundTask(services, typeof(TotpSecretReEncryptionBackgroundService));

            // La garde de l'instance unique ne tourne pas non plus : les tests lancent plusieurs API à la fois sur la même base,
            // et ses propres tests la créent avec leur verrou.
            RemoveSingleInstanceGuard(services);
        });
    }

    // Retire la garde de l'instance unique, après avoir vérifié qu'elle est branchée avant toutes les tâches de fond : branchée
    // après, une deuxième instance lancerait ses tâches avant d'être refusée.
    private static void RemoveSingleInstanceGuard(IServiceCollection services)
    {
        var hostedServices = services.Where(service => service.ServiceType == typeof(IHostedService)).ToList();
        var guard = hostedServices.FirstOrDefault(service => service.ImplementationType == typeof(SingleInstanceGuard))
            ?? throw new InvalidOperationException("Program.cs ne branche plus la garde de l'instance unique.");
        var firstTaskIndex = hostedServices.FindIndex(service => service.ImplementationType?.IsSubclassOf(typeof(RecurringBackgroundService)) == true);

        if (firstTaskIndex >= 0 && hostedServices.IndexOf(guard) > firstTaskIndex)
        {
            throw new InvalidOperationException("Program.cs doit brancher la garde de l'instance unique avant les tâches de fond.");
        }

        services.Remove(guard);
    }

    // Retire cette tâche de fond des services de l'API. Elle doit y être : une tâche que Program.cs ne branche plus ne tournerait
    // jamais, et aucun autre test ne le verrait, puisque toutes les API des tests la retirent.
    private static void RemoveBackgroundTask(IServiceCollection services, Type backgroundTaskType)
    {
        var backgroundTask = services.FirstOrDefault(service => service.ImplementationType == backgroundTaskType)
            ?? throw new InvalidOperationException("Program.cs ne branche plus la tâche de fond " + backgroundTaskType.Name + ".");

        services.Remove(backgroundTask);
    }

    // Crée le dossier des clés dans le dossier temporaire, et le fait effacer quand les tests se terminent.
    private static string CreateTestKeysFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "walletflow-tests-cles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        AppDomain.CurrentDomain.ProcessExit += (sender, arguments) => Directory.Delete(path, true);

        return path;
    }

    // Écrit un certificat RSA jetable et sa clé privée au format PEM, dans le dossier temporaire, effacés à la fin des tests.
    // Les tests des clés en créent d'autres, pour changer de certificat.
    public static (string CertificatePath, string KeyPath) WriteThrowawayCertificate(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=WalletFlow tests " + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var prefix = Path.Combine(Path.GetTempPath(), "walletflow-tests-certificat-" + name + "-" + Guid.NewGuid().ToString("N"));
        var certificatePath = prefix + ".pem";
        var keyPath = prefix + "-cle.pem";
        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        AppDomain.CurrentDomain.ProcessExit += (sender, arguments) =>
        {
            File.Delete(certificatePath);
            File.Delete(keyPath);
        };

        return (certificatePath, keyPath);
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
