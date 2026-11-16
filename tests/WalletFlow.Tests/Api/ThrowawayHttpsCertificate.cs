using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WalletFlow.Tests.Api;

// Certificat jetable des tests qui parlent HTTPS au vrai serveur Kestrel : les cookies Secure de la session ne voyagent
// qu'en HTTPS.
public static class ThrowawayHttpsCertificate
{
    // Crée un certificat pour 127.0.0.1, valable une heure, avec sa clé privée.
    public static X509Certificate2 Create()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(alternativeNames.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

        using var selfSigned = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

        // macOS ne sert en HTTPS qu'un certificat dont la clé est rangée avec lui : un passage par PKCS#12 l'y range.
        return X509CertificateLoader.LoadPkcs12(selfSigned.Export(X509ContentType.Pkcs12), null);
    }
}
