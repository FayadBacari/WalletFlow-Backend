using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Domain.BagsAndSuggestions;

// Règles d'un sac et de ses déplacements d'argent (GR Sacs, SS §5 et §6, DD §7, §11 et §12) : le nom, l'objectif,
// la progression, et les limites qui empêchent un script de remplir la base.
public static class BagRules
{
    public const int MaximumCountPerUser = 100;
    public const int MaximumAmountInCents = 1_000_000_000;
    public const int MaximumMovementsPerDay = 1000;

    // Un jour de déplacements, ce sont les dernières 24 heures, pas le jour du calendrier : la limite ne repart pas à zéro à minuit.
    public static readonly TimeSpan MovementDay = TimeSpan.FromHours(24);

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

    // Renvoie vrai si le montant d'un objectif ou d'un déplacement va de 1 centime à 1 000 000 000 centimes (DD §7, SS §5).
    public static bool IsAmountValid(int amountInCents)
    {
        return amountInCents >= 1 && amountInCents <= MaximumAmountInCents;
    }

    // Dit ce qui ne va pas dans un objectif (GR Sacs, DD §7.1) : un montant à atteindre, et une date facultative,
    // seulement avec un montant, après le jour de la saisie. Sans montant ni date, le sac n'a pas d'objectif : rien ne manque.
    public static BagGoalProblem FindGoalProblem(int? targetAmountInCents, DateOnly? targetDate, DateOnly today)
    {
        if (targetAmountInCents is int amountInCents && !IsAmountValid(amountInCents))
        {
            return BagGoalProblem.AmountBreaksRules;
        }

        if (targetDate != null && targetAmountInCents == null)
        {
            return BagGoalProblem.DateWithoutAmount;
        }

        if (targetDate is DateOnly chosenDate && chosenDate <= today)
        {
            return BagGoalProblem.DateTooEarly;
        }

        return BagGoalProblem.None;
    }

    // DD §11 : la progression est le solde du sac divisé par son objectif, en pourcentage, null sans objectif.
    // Elle est arrondie vers le bas, pour que 100 % ne soit atteint qu'une fois l'objectif vraiment atteint,
    // et elle peut dépasser 100 %. Un pourcentage trop grand pour un int s'arrête au plus grand int.
    public static int? CountProgressPercent(long balanceInCents, int? targetAmountInCents)
    {
        if (targetAmountInCents is not int target)
        {
            return null;
        }

        var percent = balanceInCents * 100 / target;

        if (percent > int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)percent;
    }
}
