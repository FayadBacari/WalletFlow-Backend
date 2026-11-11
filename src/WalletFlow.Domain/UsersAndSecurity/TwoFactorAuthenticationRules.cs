namespace WalletFlow.Domain.UsersAndSecurity;

// Règles de la double authentification (GR Auth, SS §2 et §6).
public static class TwoFactorAuthenticationRules
{
    public const int BackupCodesPerActivation = 10;
    public const int MaximumCodesPerLogin = 5;
    public const int AuthenticatorCodeLength = 6;
    public const int BackupCodeLength = 10;
    public const int BackupCodeDashPosition = 5;

    // 32 caractères possibles sur 10 positions, soit 50 bits aléatoires par code.
    // Ni I, ni L, ni O, ni U : ils se confondent à la lecture avec d'autres caractères.
    public const string BackupCodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static readonly TimeSpan WaitingLoginLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan WaitingSecretLifetime = TimeSpan.FromMinutes(10);

    // Écrit un code comme le serveur le compare : les applications l'affichent avec un espace,
    // les codes de secours sont remis avec un tiret, et la casse ne compte pas.
    public static string CleanCode(string typedCode)
    {
        var cleanedCode = "";

        foreach (var character in typedCode)
        {
            if (character != ' ' && character != '-')
            {
                cleanedCode = cleanedCode + char.ToUpperInvariant(character);
            }
        }

        return cleanedCode;
    }

    // Renvoie vrai si le texte a la forme d'un code d'application : 6 chiffres, sans chiffre d'une autre écriture.
    public static bool HasAuthenticatorCodeFormat(string cleanedCode)
    {
        if (cleanedCode.Length != AuthenticatorCodeLength)
        {
            return false;
        }

        foreach (var character in cleanedCode)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    // Renvoie vrai si le texte a la forme d'un code de secours : 10 caractères de la liste permise.
    public static bool HasBackupCodeFormat(string cleanedCode)
    {
        if (cleanedCode.Length != BackupCodeLength)
        {
            return false;
        }

        foreach (var character in cleanedCode)
        {
            if (!BackupCodeAlphabet.Contains(character))
            {
                return false;
            }
        }

        return true;
    }

    // Écrit un code de secours comme il est remis à l'utilisateur : ABCDE-12345, plus facile à recopier.
    public static string FormatBackupCode(string cleanedCode)
    {
        return cleanedCode.Substring(0, BackupCodeDashPosition)
            + "-"
            + cleanedCode.Substring(BackupCodeDashPosition);
    }
}
