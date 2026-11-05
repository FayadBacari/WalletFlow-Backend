namespace WalletFlow.Domain.UsersAndSecurity;

// Règles de la double authentification (GR Auth, SS §2 et §6).
public static class TwoFactorAuthenticationRules
{
    public const int NumberOfBackupCodesGivenAtEachActivation = 10;
    public const int MaximumNumberOfCodesTriedDuringOneLogin = 5;
    public const int NumberOfDigitsInOneAuthenticatorCode = 6;
    public const int NumberOfCharactersInOneBackupCode = 10;
    public const int NumberOfCharactersBeforeTheDashOfBackupCode = 5;

    // 32 caractères possibles sur 10 positions, soit 50 bits aléatoires par code.
    // Ni I, ni L, ni O, ni U : ils se confondent à la lecture avec d'autres caractères.
    public const string CharactersAllowedInOneBackupCode = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static readonly TimeSpan MaximumDurationOfLoginWaitingForItsCode = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumDurationOfSecretWaitingForActivation = TimeSpan.FromMinutes(10);

    // Écrit un code comme le serveur le compare : les applications l'affichent avec un espace,
    // les codes de secours sont remis avec un tiret, et la casse ne compte pas.
    public static string ConvertCodeTypedByTheUserIntoItsCleanForm(string codeTypedByTheUserInTheForm)
    {
        var codeWrittenInItsCleanForm = "";

        foreach (var characterOfTheCodeBeingCleaned in codeTypedByTheUserInTheForm)
        {
            if (characterOfTheCodeBeingCleaned != ' ' && characterOfTheCodeBeingCleaned != '-')
            {
                codeWrittenInItsCleanForm = codeWrittenInItsCleanForm + char.ToUpperInvariant(characterOfTheCodeBeingCleaned);
            }
        }

        return codeWrittenInItsCleanForm;
    }

    // Renvoie vrai si le texte a la forme d'un code d'application : 6 chiffres, sans chiffre d'une autre écriture.
    public static bool CheckThatTextHasTheFormatOfAuthenticatorCode(string codeWrittenInItsCleanForm)
    {
        if (codeWrittenInItsCleanForm.Length != NumberOfDigitsInOneAuthenticatorCode)
        {
            return false;
        }

        foreach (var characterOfTheCodeBeingChecked in codeWrittenInItsCleanForm)
        {
            if (!char.IsAsciiDigit(characterOfTheCodeBeingChecked))
            {
                return false;
            }
        }

        return true;
    }

    // Renvoie vrai si le texte a la forme d'un code de secours : 10 caractères de la liste permise.
    public static bool CheckThatTextHasTheFormatOfOneBackupCode(string codeWrittenInItsCleanForm)
    {
        if (codeWrittenInItsCleanForm.Length != NumberOfCharactersInOneBackupCode)
        {
            return false;
        }

        foreach (var characterOfTheCodeBeingChecked in codeWrittenInItsCleanForm)
        {
            if (!CharactersAllowedInOneBackupCode.Contains(characterOfTheCodeBeingChecked))
            {
                return false;
            }
        }

        return true;
    }

    // Écrit un code de secours comme il est remis à l'utilisateur : ABCDE-12345, plus facile à recopier.
    public static string ConvertBackupCodeIntoItsDisplayedForm(string backupCodeWrittenInItsCleanForm)
    {
        return backupCodeWrittenInItsCleanForm.Substring(0, NumberOfCharactersBeforeTheDashOfBackupCode)
            + "-"
            + backupCodeWrittenInItsCleanForm.Substring(NumberOfCharactersBeforeTheDashOfBackupCode);
    }
}
