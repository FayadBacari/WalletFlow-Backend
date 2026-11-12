using System.Text;

namespace WalletFlow.Domain.UsersAndSecurity;

// Règles du nom et du prénom de l'utilisateur (GR Compte, DD §3) : 1 à 50 caractères, sans caractère invisible.
// Un nom vide veut dire que l'utilisateur l'efface : il est alors enregistré vide (null).
public static class PersonalNameRules
{
    public const int MaximumLength = 50;

    // Donne la forme enregistrée : en Unicode NFC, comme un mot de passe, et sans espaces autour.
    // Un texte absent, vide ou fait seulement d'espaces efface le nom. .NET refuse de normaliser un texte qui contient
    // un caractère mal formé : il est alors gardé tel quel, et IsValid le refusera.
    public static string? Normalize(string? typedName)
    {
        if (string.IsNullOrWhiteSpace(typedName))
        {
            return null;
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

    // Renvoie vrai si un nom, déjà mis sous sa forme enregistrée, respecte les règles. Un nom effacé est toujours valable.
    // Les caractères sont comptés comme PostgreSQL les compte : un emoji compte pour un, pas pour deux.
    public static bool IsValid(string? name)
    {
        if (name == null)
        {
            return true;
        }

        var characterCount = name.EnumerateRunes().Count();

        return characterCount >= 1
            && characterCount <= MaximumLength
            && !InvisibleCharacters.AreIn(name);
    }
}
