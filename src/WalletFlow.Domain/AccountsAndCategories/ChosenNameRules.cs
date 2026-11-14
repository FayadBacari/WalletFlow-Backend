using System.Text;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Domain.AccountsAndCategories;

// Règle du nom que l'utilisateur donne lui-même à une catégorie ou à un sac (SS §5) : 1 à 50 caractères, sans caractère
// invisible. Les deux suivent exactement la même règle : elle n'est écrite qu'ici.
public static class ChosenNameRules
{
    public const int MaximumLength = 50;

    // Donne la forme enregistrée d'un nom : en Unicode NFC, comme un mot de passe, et sans espaces autour.
    // Un nom absent devient vide, et IsValid le refuse. .NET refuse de normaliser un texte qui contient
    // un caractère mal formé : il est alors gardé tel quel, et IsValid le refusera.
    public static string Normalize(string? typedName)
    {
        if (typedName == null)
        {
            return string.Empty;
        }

        try
        {
            return typedName.Normalize(NormalizationForm.FormC).Trim();
        }
        catch (ArgumentException)
        {
            return typedName.Trim();
        }
    }

    // Renvoie vrai si un nom, déjà sous sa forme enregistrée, fait de 1 à 50 caractères, sans caractère invisible.
    // Les caractères sont comptés comme PostgreSQL les compte : un emoji compte pour un, pas pour deux.
    public static bool IsValid(string name)
    {
        var characterCount = name.EnumerateRunes().Count();

        return characterCount >= 1
            && characterCount <= MaximumLength
            && !InvisibleCharacters.AreIn(name);
    }
}
