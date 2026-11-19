using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WalletFlow.Api.Configuration;

namespace WalletFlow.Tests.Api;

// Vérifie les clés qui signent le jeton anti-CSRF (SS §6 et §7) : gardées dans le dossier du réglage et chiffrées avec le
// certificat, elles survivent à un redémarrage de l'API ; hors du développement, ce dossier et ce certificat sont exigés.
public sealed class DataProtectionKeysTests : IDisposable
{
    private const string Purpose = "test-des-cles";

    private readonly string keysFolder = Path.Combine(Path.GetTempPath(), "walletflow-tests-cles-" + Guid.NewGuid().ToString("N"));

    // Efface le dossier des clés de ce test.
    public void Dispose()
    {
        if (Directory.Exists(keysFolder))
        {
            Directory.Delete(keysFolder, true);
        }
    }

    // Lance une API de production qui garde ses clés dans ce dossier, chiffrées avec le certificat des tests.
    private static WebApplicationFactory<Program> StartApi(ApiFactoryInProduction productionApi, string folder)
    {
        return productionApi.WithWebHostBuilder(builder => builder.UseSetting(DataProtectionConfiguration.KeysFolderSetting, folder));
    }

    // Lance une API de production qui garde ses clés dans ce dossier, chiffrées avec ce certificat et cette clé privée.
    private static WebApplicationFactory<Program> StartApi(ApiFactoryInProduction productionApi, string folder, string certificatePath, string keyPath)
    {
        return productionApi.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DataProtectionConfiguration.KeysFolderSetting, folder);
            builder.UseSetting(DataProtectionConfiguration.CertificatePathSetting, certificatePath);
            builder.UseSetting(DataProtectionConfiguration.CertificateKeyPathSetting, keyPath);
        });
    }

    // Crée une configuration qui ne contient que les deux réglages du certificat.
    private static IConfiguration ConfigureCertificate(string certificatePath, string keyPath)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataProtectionConfiguration.CertificatePathSetting] = certificatePath,
                [DataProtectionConfiguration.CertificateKeyPathSetting] = keyPath
            })
            .Build();
    }

    // Écrit un certificat ECDSA jetable et sa clé privée : un certificat que le chiffrement XML d'ASP.NET Core ne sait pas utiliser.
    private (string CertificatePath, string KeyPath) WriteEcdsaCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var certificate = new CertificateRequest("CN=WalletFlow tests ECDSA", key, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        Directory.CreateDirectory(keysFolder);
        var certificatePath = Path.Combine(keysFolder, "ecdsa.pem");
        var keyPath = Path.Combine(keysFolder, "ecdsa-cle.pem");
        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());

        return (certificatePath, keyPath);
    }

    // Hors du développement, chaque clé est chiffrée sur le disque avec le certificat : son fichier ne contient jamais la clé en
    // clair, seulement sa version chiffrée, que seule la clé privée du certificat ouvre.
    [Fact]
    public void KeysAreEncryptedOnDisk()
    {
        using (var productionApi = new ApiFactoryInProduction())
        using (var api = StartApi(productionApi, keysFolder))
        {
            api.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect("jeton");
        }

        var keyFile = File.ReadAllText(Assert.Single(Directory.GetFiles(keysFolder, "key-*.xml")));

        Assert.Contains("<encryptedSecret", keyFile, StringComparison.Ordinal);
        Assert.Contains("EncryptedXmlDecryptor", keyFile, StringComparison.Ordinal);
        Assert.DoesNotContain("<value>", keyFile, StringComparison.Ordinal);
        Assert.DoesNotContain("unencrypted", keyFile, StringComparison.OrdinalIgnoreCase);
    }

    // Avec un autre certificat, les anciennes clés sont illisibles : les valeurs qu'elles protégeaient sont refusées, mais l'API
    // démarre, crée une nouvelle clé et protège de nouvelles valeurs. Le site redemande simplement un jeton anti-CSRF.
    [Fact]
    public void AnotherCertificateRefusesOldValuesButKeepsTheApiWorking()
    {
        string protectedValue;
        using (var firstProductionApi = new ApiFactoryInProduction())
        using (var firstApi = StartApi(firstProductionApi, keysFolder))
        {
            protectedValue = firstApi.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect("jeton");
        }

        var otherCertificate = TestApiSettings.WriteThrowawayCertificate("autre");
        using var secondProductionApi = new ApiFactoryInProduction();
        using var secondApi = StartApi(secondProductionApi, keysFolder, otherCertificate.CertificatePath, otherCertificate.KeyPath);
        var protector = secondApi.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(protectedValue));
        Assert.Equal("nouveau", protector.Unprotect(protector.Protect("nouveau")));
        Assert.Equal(2, Directory.GetFiles(keysFolder, "key-*.xml").Length);
    }

    // Hors du développement, sans certificat, l'API refuse de démarrer, et le message nomme les réglages.
    [Fact]
    public void ProductionWithoutCertificateRefusesToStart()
    {
        using var productionApi = new ApiFactoryInProduction();
        using var apiWithoutCertificate = StartApi(productionApi, keysFolder, "", "");

        var exception = Assert.Throws<InvalidOperationException>(() => apiWithoutCertificate.CreateClient());

        Assert.Contains(DataProtectionConfiguration.CertificatePathSetting, exception.Message, StringComparison.Ordinal);
        Assert.Contains(DataProtectionConfiguration.CertificateKeyPathSetting, exception.Message, StringComparison.Ordinal);
    }

    // En développement seulement, les deux réglages peuvent manquer ensemble : les clés ne sont alors pas chiffrées.
    [Fact]
    public void DevelopmentMayGoWithoutCertificate()
    {
        Assert.Null(DataProtectionConfiguration.ReadCertificate(ConfigureCertificate("", ""), isDevelopment: true));
    }

    // Un seul des deux réglages est refusé, même en développement : le certificat et sa clé vont ensemble.
    [Theory]
    [InlineData("/tmp/certificat.pem", "")]
    [InlineData("", "/tmp/cle.pem")]
    public void OnlyOneOfTheTwoSettingsIsRefused(string certificatePath, string keyPath)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionConfiguration.ReadCertificate(ConfigureCertificate(certificatePath, keyPath), isDevelopment: true));

        Assert.Contains("exigés ensemble", exception.Message, StringComparison.Ordinal);
    }

    // Un chemin relatif dépendrait du dossier où l'API démarre : il est refusé, même quand le fichier existe à cet endroit.
    [Fact]
    public void RelativePathIsRefusedEvenWhenTheFileExists()
    {
        var certificate = TestApiSettings.WriteThrowawayCertificate("relatif");
        var relativeCertificate = Path.GetRelativePath(Directory.GetCurrentDirectory(), certificate.CertificatePath);
        var relativeKey = Path.GetRelativePath(Directory.GetCurrentDirectory(), certificate.KeyPath);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionConfiguration.ReadCertificate(ConfigureCertificate(relativeCertificate, relativeKey), isDevelopment: false));

        Assert.False(Path.IsPathFullyQualified(relativeCertificate));
        Assert.Contains("chemins absolus", exception.Message, StringComparison.Ordinal);
    }

    // Des fichiers absents sont refusés au démarrage, avec un message qui nomme les réglages sans recopier aucun contenu.
    [Fact]
    public void MissingFilesAreRefused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionConfiguration.ReadCertificate(
                ConfigureCertificate("/tmp/walletflow-certificat-absent.pem", "/tmp/walletflow-cle-absente.pem"),
                isDevelopment: false));

        Assert.Contains("illisible", exception.Message, StringComparison.Ordinal);
        Assert.Contains(DataProtectionConfiguration.CertificatePathSetting, exception.Message, StringComparison.Ordinal);
    }

    // Une clé privée qui ne va pas avec le certificat est refusée : elle ne pourrait pas relire les clés qu'il chiffre.
    [Fact]
    public void KeyOfAnotherCertificateIsRefused()
    {
        var first = TestApiSettings.WriteThrowawayCertificate("premier");
        var second = TestApiSettings.WriteThrowawayCertificate("second");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionConfiguration.ReadCertificate(ConfigureCertificate(first.CertificatePath, second.KeyPath), isDevelopment: false));

        Assert.Contains("ne vont pas ensemble", exception.Message, StringComparison.Ordinal);
    }

    // Un certificat ECDSA est refusé au démarrage : le chiffrement XML d'ASP.NET Core ne sait utiliser que RSA.
    [Fact]
    public void CertificateWithoutRsaKeyIsRefused()
    {
        var ecdsa = WriteEcdsaCertificate();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionConfiguration.ReadCertificate(ConfigureCertificate(ecdsa.CertificatePath, ecdsa.KeyPath), isDevelopment: false));

        Assert.Contains("clé RSA", exception.Message, StringComparison.Ordinal);
    }

    // Un certificat RSA et sa clé privée sont lus ensemble, prêts à chiffrer et à relire les clés.
    [Fact]
    public void RsaCertificateIsReadWithItsPrivateKey()
    {
        var certificate = TestApiSettings.WriteThrowawayCertificate("lu");

        using var read = DataProtectionConfiguration.ReadCertificate(ConfigureCertificate(certificate.CertificatePath, certificate.KeyPath), isDevelopment: false);

        Assert.NotNull(read);
        Assert.NotNull(read.GetRSAPrivateKey());
    }

    // Une valeur protégée par une API se relit après son arrêt, par une nouvelle API qui garde ses clés dans le même dossier :
    // un redéploiement ne refuse aucun jeton en cours. La clé est bien écrite dans ce dossier.
    [Fact]
    public void KeysOutlastARestartOfTheApi()
    {
        string protectedValue;
        using (var firstProductionApi = new ApiFactoryInProduction())
        using (var firstApi = StartApi(firstProductionApi, keysFolder))
        {
            protectedValue = firstApi.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect("jeton");
        }

        using var secondProductionApi = new ApiFactoryInProduction();
        using var secondApi = StartApi(secondProductionApi, keysFolder);

        Assert.Equal("jeton", secondApi.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Unprotect(protectedValue));
        Assert.NotEmpty(Directory.GetFiles(keysFolder, "key-*.xml"));
    }

    // Une API qui garde ses clés ailleurs ne relit pas la valeur : ce sont bien les clés du dossier qui la protègent.
    [Fact]
    public void ApiWithOtherKeysCannotReadTheValue()
    {
        var otherFolder = keysFolder + "-autre";
        string protectedValue;
        using (var firstProductionApi = new ApiFactoryInProduction())
        using (var firstApi = StartApi(firstProductionApi, keysFolder))
        {
            protectedValue = firstApi.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect("jeton");
        }

        try
        {
            using var otherProductionApi = new ApiFactoryInProduction();
            using var otherApi = StartApi(otherProductionApi, otherFolder);

            Assert.ThrowsAny<CryptographicException>(() =>
                otherApi.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Unprotect(protectedValue));
        }
        finally
        {
            Directory.Delete(otherFolder, true);
        }
    }

    // Le nom de l'application est fixe : les clés restent les mêmes d'un déploiement à l'autre, même si le dossier du code change.
    [Fact]
    public void ApplicationNameIsFixed()
    {
        using var productionApi = new ApiFactoryInProduction();
        using var api = StartApi(productionApi, keysFolder);

        Assert.Equal("WalletFlow", api.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    // Hors du développement, sans dossier des clés, l'API refuse de démarrer, et le message nomme le réglage.
    [Fact]
    public void ProductionWithoutKeysFolderRefusesToStart()
    {
        using var productionApi = new ApiFactoryInProduction();
        using var apiWithoutFolder = StartApi(productionApi, "");

        var exception = Assert.Throws<InvalidOperationException>(() => apiWithoutFolder.CreateClient());

        Assert.Contains(DataProtectionConfiguration.KeysFolderSetting, exception.Message, StringComparison.Ordinal);
    }

    // Le dossier doit être un chemin absolu ; en développement seulement, il peut manquer, et ASP.NET Core garde alors les clés
    // dans le dossier personnel.
    [Theory]
    [InlineData("", true, null)]
    [InlineData("/var/lib/walletflow/keys", false, "/var/lib/walletflow/keys")]
    [InlineData("/var/lib/walletflow/keys", true, "/var/lib/walletflow/keys")]
    public void KeysFolderIsReadAsAnAbsolutePath(string setting, bool isDevelopment, string? expectedFolder)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DataProtectionConfiguration.KeysFolderSetting] = setting })
            .Build();

        Assert.Equal(expectedFolder, DataProtectionConfiguration.ReadKeysFolder(configuration, isDevelopment));
    }

    // Un chemin relatif dépendrait du dossier où l'API démarre : il est refusé, comme l'absence du réglage hors du développement.
    [Theory]
    [InlineData("cles", false)]
    [InlineData("./cles", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void RelativeOrMissingFolderIsRefused(string setting, bool isDevelopment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DataProtectionConfiguration.KeysFolderSetting] = setting })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => DataProtectionConfiguration.ReadKeysFolder(configuration, isDevelopment));

        Assert.Contains(DataProtectionConfiguration.KeysFolderSetting, exception.Message, StringComparison.Ordinal);
    }
}
