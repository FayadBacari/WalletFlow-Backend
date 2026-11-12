using System.Text;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Domain.AccountsAndCategories;

// Règles d'une catégorie (GR Dépenses, SS §5 et §6, DD §6.1 et §12) : son nom, son budget,
// et le nombre de catégories d'un utilisateur. Deux noms identiques, majuscules ignorées, sont refusés par la base.
public static class CategoryRules
{
    public const int MaximumNameLength = 50;
    public const int MaximumCountPerUser = 100;
    public const int MaximumMonthlyBudgetInCents = 1_000_000_000;

    // Donne la forme enregistrée d'un nom : en Unicode NFC, comme un mot de passe, et sans espaces autour.
    // Un nom absent devient vide, et IsNameValid le refuse. .NET refuse de normaliser un texte qui contient
    // un caractère mal formé : il est alors gardé tel quel, et IsNameValid le refusera.
    public static string NormalizeName(string? typedName)
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
    public static bool IsNameValid(string name)
    {
        var characterCount = name.EnumerateRunes().Count();

        return characterCount >= 1
            && characterCount <= MaximumNameLength
            && !InvisibleCharacters.AreIn(name);
    }

    // Renvoie vrai si le budget est absent, ou s'il va de 1 centime à 1 000 000 000 centimes (DD §6.1, SS §5).
    public static bool IsMonthlyBudgetValid(int? monthlyBudgetInCents)
    {
        if (monthlyBudgetInCents == null)
        {
            return true;
        }

        return monthlyBudgetInCents >= 1
            && monthlyBudgetInCents <= MaximumMonthlyBudgetInCents;
    }
}
