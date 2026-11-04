using System.Globalization;
using System.Text;

namespace WalletFlow.Domain.UsersAndSecurity;

// Règles que doit respecter un mot de passe (GR Auth, SS §2, DD §3).
public static class PasswordRules
{
    public const int MinimumNumberOfCharactersInPassword = 12;
    public const int MaximumNumberOfCharactersInPassword = 128;

    // Renvoie vrai si le mot de passe a la bonne longueur, aucun caractère invisible et les 4 types de caractères demandés.
    public static bool CheckThatPasswordRespectsAllTheRules(string passwordToCheckAgainstTheRules)
    {
        return CheckThatPasswordHasTheRightNumberOfCharacters(passwordToCheckAgainstTheRules)
            && !CheckThatPasswordContainsAtLeastOneInvisibleCharacter(passwordToCheckAgainstTheRules)
            && CheckThatPasswordContainsTheFourRequiredTypesOfCharacters(passwordToCheckAgainstTheRules);
    }

    // Compte les vrais caractères : avec Length, un emoji compterait pour deux.
    private static bool CheckThatPasswordHasTheRightNumberOfCharacters(string passwordToCheckAgainstTheRules)
    {
        var numberOfRealCharactersInThePassword = passwordToCheckAgainstTheRules.EnumerateRunes().Count();

        return numberOfRealCharactersInThePassword >= MinimumNumberOfCharactersInPassword
            && numberOfRealCharactersInThePassword <= MaximumNumberOfCharactersInPassword;
    }

    // Renvoie vrai si le mot de passe contient un caractère invisible ou non attribué par Unicode :
    // il pourrait compter comme caractère spécial sans se voir, et on ne pourrait pas le retaper.
    private static bool CheckThatPasswordContainsAtLeastOneInvisibleCharacter(string passwordToCheckAgainstTheRules)
    {
        foreach (var realCharacterOfThePasswordBeingChecked in passwordToCheckAgainstTheRules.EnumerateRunes())
        {
            var unicodeCategoryOfTheCharacterBeingChecked = Rune.GetUnicodeCategory(realCharacterOfThePasswordBeingChecked);

            if (unicodeCategoryOfTheCharacterBeingChecked == UnicodeCategory.Control
                || unicodeCategoryOfTheCharacterBeingChecked == UnicodeCategory.Format
                || unicodeCategoryOfTheCharacterBeingChecked == UnicodeCategory.LineSeparator
                || unicodeCategoryOfTheCharacterBeingChecked == UnicodeCategory.ParagraphSeparator
                || unicodeCategoryOfTheCharacterBeingChecked == UnicodeCategory.PrivateUse
                || unicodeCategoryOfTheCharacterBeingChecked == UnicodeCategory.OtherNotAssigned)
            {
                return true;
            }
        }

        return false;
    }

    // Renvoie vrai si le mot de passe contient une majuscule, une minuscule, un chiffre et un caractère spécial.
    private static bool CheckThatPasswordContainsTheFourRequiredTypesOfCharacters(string passwordToCheckAgainstTheRules)
    {
        var passwordContainsAtLeastOneUppercaseLetter = false;
        var passwordContainsAtLeastOneLowercaseLetter = false;
        var passwordContainsAtLeastOneDigitCharacter = false;
        var passwordContainsAtLeastOneSpecialCharacter = false;

        foreach (var realCharacterOfThePasswordBeingChecked in passwordToCheckAgainstTheRules.EnumerateRunes())
        {
            if (Rune.IsUpper(realCharacterOfThePasswordBeingChecked))
            {
                passwordContainsAtLeastOneUppercaseLetter = true;
            }
            else if (Rune.IsLower(realCharacterOfThePasswordBeingChecked))
            {
                passwordContainsAtLeastOneLowercaseLetter = true;
            }
            else if (Rune.IsDigit(realCharacterOfThePasswordBeingChecked))
            {
                passwordContainsAtLeastOneDigitCharacter = true;
            }
            else if (CheckThatCharacterCountsAsSpecialCharacter(realCharacterOfThePasswordBeingChecked))
            {
                passwordContainsAtLeastOneSpecialCharacter = true;
            }
        }

        return passwordContainsAtLeastOneUppercaseLetter
            && passwordContainsAtLeastOneLowercaseLetter
            && passwordContainsAtLeastOneDigitCharacter
            && passwordContainsAtLeastOneSpecialCharacter;
    }

    // Un caractère spécial n'est ni une lettre ni un accent combinant, qui s'ajoute simplement à une lettre.
    private static bool CheckThatCharacterCountsAsSpecialCharacter(Rune realCharacterOfThePasswordBeingChecked)
    {
        var unicodeCategoryOfTheCharacterBeingChecked = Rune.GetUnicodeCategory(realCharacterOfThePasswordBeingChecked);

        return !Rune.IsLetter(realCharacterOfThePasswordBeingChecked)
            && unicodeCategoryOfTheCharacterBeingChecked != UnicodeCategory.NonSpacingMark
            && unicodeCategoryOfTheCharacterBeingChecked != UnicodeCategory.SpacingCombiningMark
            && unicodeCategoryOfTheCharacterBeingChecked != UnicodeCategory.EnclosingMark;
    }
}
