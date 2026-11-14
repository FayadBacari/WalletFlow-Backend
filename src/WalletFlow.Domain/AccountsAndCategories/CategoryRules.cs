namespace WalletFlow.Domain.AccountsAndCategories;

// Règles d'une catégorie (GR Dépenses, SS §5 et §6, DD §6.1 et §12) : son nom, son budget,
// et le nombre de catégories d'un utilisateur. Deux noms identiques, majuscules ignorées, sont refusés par la base.
public static class CategoryRules
{
    public const int MaximumNameLength = ChosenNameRules.MaximumLength;
    public const int MaximumCountPerUser = 100;
    public const int MaximumMonthlyBudgetInCents = 1_000_000_000;

    // Donne la forme enregistrée d'un nom : la règle commune aux noms choisis par l'utilisateur (voir ChosenNameRules).
    public static string NormalizeName(string? typedName)
    {
        return ChosenNameRules.Normalize(typedName);
    }

    // Renvoie vrai si un nom, déjà sous sa forme enregistrée, fait de 1 à 50 caractères, sans caractère invisible (SS §5).
    public static bool IsNameValid(string name)
    {
        return ChosenNameRules.IsValid(name);
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
