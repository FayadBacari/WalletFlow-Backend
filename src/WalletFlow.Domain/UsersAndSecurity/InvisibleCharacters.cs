using System.Globalization;
using System.Text;

namespace WalletFlow.Domain.UsersAndSecurity;

// Caractères qu'on ne voit pas à l'écran : caractères de contrôle (dont les retours à la ligne), de mise en forme,
// séparateurs de ligne, zones privées, caractères non attribués par Unicode, et morceaux de caractère mal formés.
// Un texte qui en contient pourrait tromper un lecteur, compter un caractère qui ne se voit pas, ou ne plus se retaper (DD §3).
public static class InvisibleCharacters
{
    // Renvoie vrai si le texte contient au moins un de ces caractères.
    public static bool AreIn(string text)
    {
        foreach (var character in text.EnumerateRunes())
        {
            if (IsInvisible(character))
            {
                return true;
            }
        }

        return false;
    }

    // Retire ces caractères d'un texte reçu d'ailleurs, par exemple d'une banque, au lieu de le refuser.
    // Un morceau de caractère mal formé disparaît aussi : le texte rendu est toujours bien formé.
    public static string Remove(string text)
    {
        var visibleText = new StringBuilder();

        foreach (var character in text.EnumerateRunes())
        {
            if (!IsInvisible(character))
            {
                visibleText.Append(character.ToString());
            }
        }

        return visibleText.ToString();
    }

    // Un morceau de caractère mal formé est lu par .NET comme le caractère de remplacement U+FFFD : il est refusé lui aussi.
    private static bool IsInvisible(Rune character)
    {
        var category = Rune.GetUnicodeCategory(character);

        return character == Rune.ReplacementChar
            || category == UnicodeCategory.Control
            || category == UnicodeCategory.Format
            || category == UnicodeCategory.LineSeparator
            || category == UnicodeCategory.ParagraphSeparator
            || category == UnicodeCategory.PrivateUse
            || category == UnicodeCategory.OtherNotAssigned;
    }
}
