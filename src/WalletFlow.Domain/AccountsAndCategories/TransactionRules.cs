using System.Text;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Domain.AccountsAndCategories;

// Règles des opérations reçues de la banque (GR Comptes, SS §5, DD §5.3).
public static class TransactionRules
{
    public const int MaximumLabelLength = 255;
    public const int MaximumAmountInCents = 1_000_000_000;
    public const int MaximumExternalIdLength = 100;
    public const string LabelReplacement = "Opération bancaire";

    // Une opération date au plus tôt du 1er janvier 2000 (DD §5.3).
    public static readonly DateOnly EarliestDate = new DateOnly(2000, 1, 1);

    // La première lecture d'un compte remonte un an en arrière : la banque donne le plus d'historique juste après
    // l'autorisation, et un an couvre les graphiques par année et les moyennes sur 3 mois.
    public const int FirstReadingDays = 365;

    // Les lectures suivantes repartent de la dernière opération connue, moins 7 jours : une opération comptabilisée
    // en retard, à une date déjà passée, arrive quand même. Une opération déjà reçue n'est jamais ajoutée deux fois.
    public const int ReadingOverlapDays = 7;

    // Renvoie vrai si le montant est permis : non nul, et au plus 1 000 000 000 centimes en valeur absolue (DD §5.3).
    public static bool IsAmountValid(long amountInCents)
    {
        return amountInCents != 0
            && amountInCents >= -MaximumAmountInCents
            && amountInCents <= MaximumAmountInCents;
    }

    // Renvoie vrai si la date est permise : à partir du 1er janvier 2000 (DD §5.3).
    public static bool IsDateValid(DateOnly date)
    {
        return date >= EarliestDate;
    }

    // Choisit le jour d'où lire les opérations d'un compte : un an en arrière s'il n'en a aucune, sinon sa dernière opération
    // moins 7 jours. La lecture ne remonte jamais plus d'un an, et ne commence jamais après aujourd'hui :
    // une opération datée dans le futur ne bloque pas les lectures suivantes.
    public static DateOnly ChooseReadingStart(DateOnly? latestKnownDate, DateOnly today)
    {
        var oldestStart = today.AddDays(-FirstReadingDays);

        if (latestKnownDate is not DateOnly latestDate)
        {
            return oldestStart;
        }

        var start = latestDate.AddDays(-ReadingOverlapDays);

        if (start < oldestStart)
        {
            return oldestStart;
        }

        if (start > today)
        {
            return today;
        }

        return start;
    }

    // Donne la forme enregistrée d'un libellé reçu de la banque, contenu non fiable (SS §5) : chaque suite d'espaces
    // ou de retours à la ligne devient un seul espace, les caractères invisibles disparaissent, puis le texte passe
    // en Unicode NFC et est coupé à 255 caractères (voir BankConnectionRules). Un libellé vide devient « Opération bancaire ».
    public static string CleanLabel(string? receivedLabel)
    {
        var visibleLabel = new StringBuilder();
        var isSpaceWaiting = false;

        foreach (var character in (receivedLabel ?? string.Empty).EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(character))
            {
                isSpaceWaiting = visibleLabel.Length > 0;
                continue;
            }

            if (InvisibleCharacters.IsInvisible(character))
            {
                continue;
            }

            if (isSpaceWaiting)
            {
                visibleLabel.Append(' ');
                isSpaceWaiting = false;
            }

            visibleLabel.Append(character.ToString());
        }

        return BankConnectionRules.CleanReceivedName(visibleLabel.ToString(), MaximumLabelLength, LabelReplacement);
    }
}
