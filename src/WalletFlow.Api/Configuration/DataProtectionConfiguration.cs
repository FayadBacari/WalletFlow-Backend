using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace WalletFlow.Api.Configuration;

// Les clés de la protection des données d'ASP.NET Core, qui signent le jeton anti-CSRF (SS §6). Hors du développement, elles sont
// gardées dans un dossier qui survit aux redéploiements, par exemple un volume monté sur un disque chiffré (SS §7) : sans lui,
// chaque redéploiement les perdrait, et tous les jetons anti-CSRF en cours seraient refusés. Le nom de l'application est fixe :
// les clés restent les mêmes d'un déploiement à l'autre, même si le dossier du code change. Hors du développement, elles sont aussi
// chiffrées sur le disque avec un certificat X.509 (SS §7) : un fichier de clés copié ne sert à rien sans la clé privée du
// certificat, gardée à part. Changer de certificat rend les anciennes clés illisibles : l'API en crée une nouvelle, et chaque
// jeton anti-CSRF en cours est refusé une fois, le temps que le site en redemande un.
public static class DataProtectionConfiguration
{
    public const string KeysFolderSetting = "DataProtection:KeysFolder";
    public const string CertificatePathSetting = "DataProtection:CertificatePath";
    public const string CertificateKeyPathSetting = "DataProtection:CertificateKeyPath";
    public const string ApplicationName = "WalletFlow";

    // Lit le dossier des clés : un chemin absolu. En développement, il peut manquer : ASP.NET Core garde alors les clés dans le
    // dossier personnel. Ailleurs, son absence empêche l'API de démarrer.
    public static string? ReadKeysFolder(IConfiguration configuration, bool isDevelopment)
    {
        var keysFolder = configuration[KeysFolderSetting];

        if (string.IsNullOrWhiteSpace(keysFolder))
        {
            if (isDevelopment)
            {
                return null;
            }

            throw new InvalidOperationException(
                "Le réglage « " + KeysFolderSetting + " » est absent : hors du développement, les clés du jeton anti-CSRF doivent être "
                + "gardées dans un dossier qui survit aux redéploiements.");
        }

        if (!Path.IsPathFullyQualified(keysFolder))
        {
            throw new InvalidOperationException("Le réglage « " + KeysFolderSetting + " » doit être un chemin absolu, comme /var/lib/walletflow/keys.");
        }

        return keysFolder;
    }

    // Lit le certificat qui chiffre les clés : son fichier et celui de sa clé privée, au format PEM, par deux chemins absolus. En
    // développement, les deux réglages peuvent manquer ensemble. Ailleurs, leur absence empêche l'API de démarrer, comme un
    // certificat illisible ou sans clé RSA : le chiffrement XML d'ASP.NET Core ne sait utiliser que RSA.
    public static X509Certificate2? ReadCertificate(IConfiguration configuration, bool isDevelopment)
    {
        var certificatePath = configuration[CertificatePathSetting];
        var keyPath = configuration[CertificateKeyPathSetting];

        if (string.IsNullOrWhiteSpace(certificatePath) && string.IsNullOrWhiteSpace(keyPath) && isDevelopment)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(keyPath))
        {
            throw new InvalidOperationException(
                "Les réglages « " + CertificatePathSetting + " » et « " + CertificateKeyPathSetting + " » sont exigés ensemble : hors du "
                + "développement, les clés du jeton anti-CSRF sont chiffrées sur le disque avec ce certificat et sa clé privée.");
        }

        if (!Path.IsPathFullyQualified(certificatePath) || !Path.IsPathFullyQualified(keyPath))
        {
            throw new InvalidOperationException(
                "Les réglages « " + CertificatePathSetting + " » et « " + CertificateKeyPathSetting + " » doivent être des chemins absolus.");
        }

        X509Certificate2 certificate;

        try
        {
            certificate = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidOperationException(
                "Le certificat de « " + CertificatePathSetting + " » ou sa clé de « " + CertificateKeyPathSetting + " » est illisible, "
                + "ou ils ne vont pas ensemble.",
                exception);
        }

        if (certificate.GetRSAPrivateKey() == null)
        {
            certificate.Dispose();
            throw new InvalidOperationException("Le certificat de « " + CertificatePathSetting + " » doit avoir une clé RSA.");
        }

        return certificate;
    }

    // Ajoute la protection des données de l'API : son nom fixe, le dossier de ses clés et le certificat qui les chiffre, s'ils
    // sont donnés.
    public static IServiceCollection AddWalletFlowDataProtection(this IServiceCollection services, string? keysFolder, X509Certificate2? certificate)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName(ApplicationName);

        if (keysFolder != null)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysFolder));
        }

        if (certificate != null)
        {
            dataProtection.ProtectKeysWithCertificate(certificate);
        }

        return services;
    }
}
