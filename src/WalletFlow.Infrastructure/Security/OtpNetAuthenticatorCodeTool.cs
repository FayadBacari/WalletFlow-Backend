using System.Security.Cryptography;
using OtpNet;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Security;

// Calcule les codes de l'application d'authentification avec Otp.NET, une bibliothèque reconnue (SS §2 et §13).
public class OtpNetAuthenticatorCodeTool : IAuthenticatorCodeTool
{
    private const string NameOfTheIssuerShownInTheAuthenticatorApplication = "WalletFlow";

    // 20 octets, soit 160 bits aléatoires : la taille recommandée par la RFC 4226.
    private const int NumberOfRandomBytesInTheAuthenticatorSecret = 20;

    private const int NumberOfSecondsInOneAuthenticatorPeriod = 30;

    // Tire au hasard un nouveau secret avec le générateur cryptographique de .NET, et l'écrit en Base32.
    public string GenerateNewRandomSecretForTheAuthenticatorApplication()
    {
        return Base32Encoding.ToString(RandomNumberGenerator.GetBytes(NumberOfRandomBytesInTheAuthenticatorSecret));
    }

    // Donne le lien otpauth://. Otp.NET se charge d'encoder les caractères spéciaux de l'adresse e-mail.
    public string BuildLinkThatAddsThisSecretToTheApplication(string secretSharedWithTheAuthenticatorApplication, string emailAddressOfTheUserAccount)
    {
        return new OtpUri(OtpType.Totp, secretSharedWithTheAuthenticatorApplication, emailAddressOfTheUserAccount, NameOfTheIssuerShownInTheAuthenticatorApplication).ToString();
    }

    // Compare le code à ceux de la période en cours, de la précédente et de la suivante.
    // Sans ce réglage, Otp.NET n'accepterait que la période en cours. Il lit la date telle quelle, sans regarder son fuseau : elle doit donc être en UTC.
    public long? FindPeriodWhereThisCodeIsValidAtThisDate(string secretSharedWithTheAuthenticatorApplication, string authenticatorCodeTypedByTheUserToCheckNow, DateTime dateOfTheCodeCheckInUtc)
    {
        var calculatorOfTheCodesOfTheAuthenticatorApplication = new Totp(
            Base32Encoding.ToBytes(secretSharedWithTheAuthenticatorApplication),
            NumberOfSecondsInOneAuthenticatorPeriod,
            OtpHashMode.Sha1,
            TwoFactorAuthenticationRules.NumberOfDigitsInOneAuthenticatorCode);

        var codeIsValidInOneOfThePeriods = calculatorOfTheCodesOfTheAuthenticatorApplication.VerifyTotp(
            dateOfTheCodeCheckInUtc,
            authenticatorCodeTypedByTheUserToCheckNow,
            out var numberOfThePeriodWhereTheCodeIsValid,
            VerificationWindow.RfcSpecifiedNetworkDelay);

        if (!codeIsValidInOneOfThePeriods)
        {
            return null;
        }

        return numberOfThePeriodWhereTheCodeIsValid;
    }
}
