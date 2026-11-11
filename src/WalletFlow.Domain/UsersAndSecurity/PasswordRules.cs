using System.Globalization;
using System.Text;

namespace WalletFlow.Domain.UsersAndSecurity;

// Règles que doit respecter un mot de passe (GR Auth, SS §2, DD §3).
public static class PasswordRules
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;

    // Écrit le mot de passe en Unicode NFC, avant les règles et le hachage (SS §2) : tapé sur deux appareils
    // différents, un « é » donne le même résultat. .NET refuse de normaliser un texte qui contient des
    // caractères interdits : il est alors gardé tel quel, et il ne pourra ni être choisi ni correspondre à un compte.
    public static string Normalize(string password)
    {
        try
        {
            return password.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return password;
        }
    }

    // Renvoie vrai si le mot de passe a la bonne longueur, aucun caractère invisible et les 4 types de caractères demandés.
    public static bool IsValid(string password)
    {
        return HasValidLength(password)
            && !ContainsInvisibleCharacter(password)
            && HasRequiredCharacterTypes(password);
    }

    // Compte les vrais caractères : avec Length, un emoji compterait pour deux.
    private static bool HasValidLength(string password)
    {
        var characterCount = password.EnumerateRunes().Count();

        return characterCount >= MinimumLength
            && characterCount <= MaximumLength;
    }

    // Renvoie vrai si le mot de passe contient un caractère invisible ou non attribué par Unicode :
    // il pourrait compter comme caractère spécial sans se voir, et on ne pourrait pas le retaper.
    private static bool ContainsInvisibleCharacter(string password)
    {
        foreach (var character in password.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(character);

            if (category == UnicodeCategory.Control
                || category == UnicodeCategory.Format
                || category == UnicodeCategory.LineSeparator
                || category == UnicodeCategory.ParagraphSeparator
                || category == UnicodeCategory.PrivateUse
                || category == UnicodeCategory.OtherNotAssigned)
            {
                return true;
            }
        }

        return false;
    }

    // Renvoie vrai si le mot de passe contient une majuscule, une minuscule, un chiffre et un caractère spécial.
    private static bool HasRequiredCharacterTypes(string password)
    {
        var hasUppercase = false;
        var hasLowercase = false;
        var hasDigit = false;
        var hasSpecialCharacter = false;

        foreach (var character in password.EnumerateRunes())
        {
            if (Rune.IsUpper(character))
            {
                hasUppercase = true;
            }
            else if (Rune.IsLower(character))
            {
                hasLowercase = true;
            }
            else if (Rune.IsDigit(character))
            {
                hasDigit = true;
            }
            else if (IsSpecialCharacter(character))
            {
                hasSpecialCharacter = true;
            }
        }

        return hasUppercase
            && hasLowercase
            && hasDigit
            && hasSpecialCharacter;
    }

    // Un caractère spécial n'est ni une lettre ni un accent combinant, qui s'ajoute simplement à une lettre.
    private static bool IsSpecialCharacter(Rune character)
    {
        var category = Rune.GetUnicodeCategory(character);

        return !Rune.IsLetter(character)
            && category != UnicodeCategory.NonSpacingMark
            && category != UnicodeCategory.SpacingCombiningMark
            && category != UnicodeCategory.EnclosingMark;
    }
}
