using System.Security.Cryptography;
using OtpNet;

namespace WalletFlow.Tests.Fakes;

// Outils communs aux tests de la double authentification : ils font ce que ferait l'application du téléphone.
public static class TwoFactorTestTools
{
    public const int NumberOfSecondsInOneAuthenticatorPeriod = 30;

    // Tire une clé de chiffrement pour un test : aucune clé n'entre donc dans Git.
    public static string CreateNewEncryptionKeyForOneTest()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    // Calcule le code qu'afficherait une application d'authentification à cette date.
    public static string CalculateAuthenticatorCodeShownAtThisDate(string secretSharedWithTheAuthenticatorApplication, DateTimeOffset dateShownOnThePhoneOfTheUser)
    {
        return new Totp(Base32Encoding.ToBytes(secretSharedWithTheAuthenticatorApplication)).ComputeTotp(dateShownOnThePhoneOfTheUser.UtcDateTime);
    }

    // Donne un code à 6 chiffres qui n'est le bon code d'aucune des trois périodes acceptées.
    public static string FindAuthenticatorCodeThatIsWrongAtThisDate(string secretSharedWithTheAuthenticatorApplication, DateTimeOffset dateOfTheCodeCheckInTheTest)
    {
        var codesOfThePhoneAcceptedAtThisDate = new List<string>();

        for (var numberOfPeriodsFromTheCurrentOne = -1; numberOfPeriodsFromTheCurrentOne <= 1; numberOfPeriodsFromTheCurrentOne++)
        {
            codesOfThePhoneAcceptedAtThisDate.Add(CalculateAuthenticatorCodeShownAtThisDate(
                secretSharedWithTheAuthenticatorApplication,
                dateOfTheCodeCheckInTheTest.AddSeconds(numberOfPeriodsFromTheCurrentOne * NumberOfSecondsInOneAuthenticatorPeriod)));
        }

        var numberTriedAsAWrongCode = 0;
        while (codesOfThePhoneAcceptedAtThisDate.Contains(numberTriedAsAWrongCode.ToString("D6")))
        {
            numberTriedAsAWrongCode = numberTriedAsAWrongCode + 1;
        }

        return numberTriedAsAWrongCode.ToString("D6");
    }
}
