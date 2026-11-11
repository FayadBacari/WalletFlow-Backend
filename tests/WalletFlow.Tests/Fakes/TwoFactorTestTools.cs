using System.Security.Cryptography;
using OtpNet;

namespace WalletFlow.Tests.Fakes;

// Outils communs aux tests de la double authentification : ils font ce que ferait l'application du téléphone.
public static class TwoFactorTestTools
{
    public const int PeriodInSeconds = 30;

    // Tire une clé de chiffrement pour un test : aucune clé n'entre donc dans Git.
    public static string CreateEncryptionKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    // Calcule le code qu'afficherait une application d'authentification à cette date.
    public static string CalculateCodeAt(string secret, DateTimeOffset date)
    {
        return new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(date.UtcDateTime);
    }

    // Donne un code à 6 chiffres qui n'est le bon code d'aucune des trois périodes acceptées.
    public static string FindWrongCodeAt(string secret, DateTimeOffset checkDate)
    {
        var acceptedCodes = new List<string>();

        for (var periodOffset = -1; periodOffset <= 1; periodOffset++)
        {
            acceptedCodes.Add(CalculateCodeAt(
                secret,
                checkDate.AddSeconds(periodOffset * PeriodInSeconds)));
        }

        var candidate = 0;
        while (acceptedCodes.Contains(candidate.ToString("D6")))
        {
            candidate = candidate + 1;
        }

        return candidate.ToString("D6");
    }
}
