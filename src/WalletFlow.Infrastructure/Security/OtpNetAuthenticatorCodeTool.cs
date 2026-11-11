using System.Security.Cryptography;
using OtpNet;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Security;

// Calcule les codes de l'application d'authentification avec Otp.NET, une bibliothèque reconnue (SS §2 et §13).
public class OtpNetAuthenticatorCodeTool : IAuthenticatorCodeTool
{
    private const string IssuerName = "WalletFlow";

    // 20 octets, soit 160 bits aléatoires : la taille recommandée par la RFC 4226.
    private const int SecretByteCount = 20;

    private const int PeriodInSeconds = 30;

    // Tire au hasard un nouveau secret avec le générateur cryptographique de .NET, et l'écrit en Base32.
    public string GenerateSecret()
    {
        return Base32Encoding.ToString(RandomNumberGenerator.GetBytes(SecretByteCount));
    }

    // Donne le lien otpauth://. Otp.NET se charge d'encoder les caractères spéciaux de l'adresse e-mail.
    public string BuildSetupLink(string secret, string email)
    {
        return new OtpUri(OtpType.Totp, secret, email, IssuerName).ToString();
    }

    // Compare le code à ceux de la période en cours, de la précédente et de la suivante.
    // Sans ce réglage, Otp.NET n'accepterait que la période en cours. Il lit la date telle quelle, sans regarder son fuseau : elle doit donc être en UTC.
    public long? FindValidPeriod(string secret, string code, DateTime now)
    {
        var totp = new Totp(
            Base32Encoding.ToBytes(secret),
            PeriodInSeconds,
            OtpHashMode.Sha1,
            TwoFactorAuthenticationRules.AuthenticatorCodeLength);

        var isValid = totp.VerifyTotp(
            now,
            code,
            out var period,
            VerificationWindow.RfcSpecifiedNetworkDelay);

        if (!isValid)
        {
            return null;
        }

        return period;
    }
}
