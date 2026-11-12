using System.Globalization;
using System.Text;

namespace WalletFlow.Domain.AccountsAndCategories;

// La forme simple d'un libellé, qui sert à reconnaître « le même libellé » (GR Dépenses, DD §6.2) : seulement ses lettres,
// en majuscules et sans accents, ses mots séparés par un seul espace. Les chiffres, la ponctuation et les espaces en trop
// disparaissent : « CB Carrefour 12/11 » et « CB CARREFOUR 03/12 » ont la même forme simple, « CB CARREFOUR ».
public static class TransactionLabels
{
    // Donne la forme simple d'un libellé. Un libellé sans aucune lettre donne un texte vide.
    public static string Simplify(string label)
    {
        var simpleLabel = new StringBuilder();
        var isSpaceWaiting = false;

        foreach (var character in SeparateAccents(label).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(character);

            // L'accent séparé de sa lettre disparaît : « É » devient « E ».
            if (category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.SpacingCombiningMark
                || category == UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (!Rune.IsLetter(character))
            {
                isSpaceWaiting = simpleLabel.Length > 0;
                continue;
            }

            if (isSpaceWaiting)
            {
                simpleLabel.Append(' ');
                isSpaceWaiting = false;
            }

            simpleLabel.Append(Rune.ToUpperInvariant(character).ToString());
        }

        return simpleLabel.ToString();
    }

    // Renvoie vrai si ces mots, sous forme simple, sont des mots entiers de ce libellé sous forme simple :
    // « CARREFOUR » est dans « CB CARREFOUR MARKET », mais pas dans « CARREFOURS ».
    public static bool ContainsWords(string simpleLabel, string simpleWords)
    {
        if (simpleWords.Length == 0)
        {
            return false;
        }

        return (" " + simpleLabel + " ").Contains(" " + simpleWords + " ", StringComparison.Ordinal);
    }

    // Sépare chaque accent de sa lettre (Unicode NFD). .NET refuse de normaliser un texte mal formé :
    // il est alors gardé tel quel, et ses caractères mal formés, qui ne sont pas des lettres, deviennent des espaces.
    private static string SeparateAccents(string label)
    {
        try
        {
            return label.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            return label;
        }
    }
}
